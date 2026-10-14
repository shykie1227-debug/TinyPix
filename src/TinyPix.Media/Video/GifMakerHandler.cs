using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TinyPix.Core.Jobs;
using TinyPix.Core.Tools;
using TinyPix.Media.Ffmpeg;

namespace TinyPix.Media.Video;

/// <summary>
/// GIF 制作 handler (video.gif). Implements the two-pass FFmpeg pipeline required
/// for paletted GIF output:
///   1) palettegen  -&gt; a temporary palette PNG (max_colors from the quality slider,
///      stats_mode=diff to favour moving detail);
///   2) paletteuse  -&gt; {原名}_GIF.gif (dither=sierra2_4a, loop count, fps).
/// Reuses VideoOutputHandlerBase for engine resolution, file-existence validation,
/// and the {原名}_GIF.gif collision-aware naming, overriding only the encode path
/// and the GIF-specific time/fps/scale validation.
/// </summary>
public sealed class GifMakerHandler : VideoOutputHandlerBase
{
    // FEATURE-MATRIX frozen authority caps the GIF clip at 30s (MEDIA_CAPABILITIES
    // says 60s; the frozen matrix wins, consistent with how Phase 1 was decided).
    private const int MaxDurationSeconds = 30;
    private const int DefaultFps = 12;
    private const int DefaultWidth = 640;
    private const int DefaultLoop = 0; // 0 == infinite loop

    public GifMakerHandler(IFfmpegEngineProvider engine) : base(engine) { }

    public override string ToolId => "video.gif";

    protected override string OutputSuffix => "_GIF";

    /// <summary>
    /// A GIF tool always produces .gif, so the output name must not depend on a
    /// <c>format</c> parameter the caller may legitimately omit.
    /// </summary>
    protected override string DefaultExtension => "gif";

    public override async ValueTask<ToolValidationResult> ValidateAsync(
        JobRequest request,
        CancellationToken cancellationToken)
    {
        // Base: format supported, quality in 0–100, every input file exists.
        ToolValidationResult baseResult = await base.ValidateAsync(request, cancellationToken);
        if (!baseResult.IsValid)
        {
            return baseResult;
        }

        if (!string.Equals(
                request.Parameters.TryGetValue("format", out string? format) ? format : null,
                "gif",
                StringComparison.OrdinalIgnoreCase))
        {
            return ToolValidationResult.Invalid(new JobError(
                "video.gif.format", "GIF 制作仅支持 gif 输出格式。", false));
        }

        double start = ParseDouble(request.Parameters, "startSecs", 0);
        double end = ParseDouble(request.Parameters, "endSecs", 0);
        double duration = end > start ? end - start : 0;

        if (end <= start)
        {
            return ToolValidationResult.Invalid(new JobError(
                "video.gif.range", "结束时间必须大于开始时间。", false));
        }

        if (duration <= 0 || duration > MaxDurationSeconds)
        {
            return ToolValidationResult.Invalid(new JobError(
                "video.gif.duration",
                $"GIF 时长必须在 0–{MaxDurationSeconds} 秒之间。", false));
        }

        return ToolValidationResult.Valid();
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(
        JobExecutionContext context,
        CancellationToken cancellationToken)
    {
        string enginePath = Engine.FfmpegExecutablePath;
        if (string.IsNullOrWhiteSpace(enginePath) || !File.Exists(enginePath))
        {
            throw new InvalidOperationException($"无法定位本地媒体引擎：{enginePath ?? "(空)"}");
        }

        var outputs = new List<string>();
        foreach (string input in context.Request.InputPaths)
        {
            string outputPath = PreviewOutputPath(input, context.Request.OutputDirectory, context.Request.Parameters);
            string palettePath = PalettePath(outputPath);
            var progress = new GifProgressAdapter(context.Progress);

            // Pass 1 — generate the palette.
            IReadOnlyList<string> paletteArgs = BuildPaletteArguments(input, palettePath, context.Request.Parameters);
            FfmpegProcessResult paletteResult = await new FfmpegProcessRunner().RunAsync(
                enginePath, paletteArgs, duration: null, progress, cancellationToken).ConfigureAwait(false);
            if (paletteResult.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"调色板生成失败（退出码 {paletteResult.ExitCode}）。{Truncate(paletteResult.StandardError)}");
            }

            // Pass 2 — render the GIF from the palette.
            IReadOnlyList<string> gifArgs = BuildArguments(input, outputPath, context.Request.Parameters);
            double clipSeconds = ClipDuration(context.Request.Parameters);
            FfmpegProcessResult gifResult = await new FfmpegProcessRunner().RunAsync(
                enginePath, gifArgs, TimeSpan.FromSeconds(clipSeconds), progress, cancellationToken).ConfigureAwait(false);
            if (gifResult.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"GIF 生成失败（退出码 {gifResult.ExitCode}）。{Truncate(gifResult.StandardError)}");
            }

            outputs.Add(outputPath);
        }

        return ToolExecutionResult.Success(outputs);
    }

    /// <summary>
    /// Builds the second-pass (paletteuse) argument list. Also used by
    /// PreviewArguments / the contract tests: it references the deterministic
    /// palette path derived from the output file.
    /// </summary>
    protected override IReadOnlyList<string> BuildArguments(
        string input,
        string output,
        IReadOnlyDictionary<string, string> parameters)
    {
        int fps = ParseInt(parameters, "fps", DefaultFps);
        int loop = ParseInt(parameters, "loopCount", DefaultLoop);

        var args = new List<string>
        {
            "-y",
            "-i", input,
            "-i", PalettePath(output),
            "-lavfi",
            BuildFilter(parameters) + ",paletteuse=dither=sierra2_4a",
            "-loop", loop.ToString(CultureInfo.InvariantCulture),
            "-r", fps.ToString(CultureInfo.InvariantCulture),
            output,
        };
        return args;
    }

    private IReadOnlyList<string> BuildPaletteArguments(
        string input,
        string palettePath,
        IReadOnlyDictionary<string, string> parameters)
    {
        int colors = PaletteColors(parameters);
        var args = new List<string>
        {
            "-y",
            "-i", input,
            "-vf",
            BuildFilter(parameters) + $",palettegen=max_colors={colors}:stats_mode=diff",
            palettePath,
        };
        return args;
    }

    private static string BuildFilter(IReadOnlyDictionary<string, string> parameters)
    {
        var segments = new List<string>();

        double start = ParseDouble(parameters, "startSecs", 0);
        double end = ParseDouble(parameters, "endSecs", 0);
        if (end > start)
        {
            // Keep the trim in the filter graph so both passes share the same window.
            segments.Add($"trim=start={start.ToString(CultureInfo.InvariantCulture)}:end={end.ToString(CultureInfo.InvariantCulture)},setpts=PTS-STARTPTS");
        }

        int fps = ParseInt(parameters, "fps", DefaultFps);
        segments.Add($"fps={fps}");

        int width = ParseInt(parameters, "width", DefaultWidth);
        if (width > 0)
        {
            // -2 keeps the height even, which the GIF/palette filters require.
            segments.Add($"scale={width}:-2");
        }

        return string.Join(",", segments);
    }

    private static int PaletteColors(IReadOnlyDictionary<string, string> parameters)
    {
        int quality = ParseInt(parameters, "quality", 80);
        quality = Math.Clamp(quality, 0, 100);
        // Map 0–100 quality to 32–256 palette colors.
        return Math.Clamp((int)Math.Round(quality * 2.56), 32, 256);
    }

    private static double ClipDuration(IReadOnlyDictionary<string, string> parameters)
    {
        double start = ParseDouble(parameters, "startSecs", 0);
        double end = ParseDouble(parameters, "endSecs", 0);
        return end > start ? end - start : 0;
    }

    private static string PalettePath(string outputPath) =>
        Path.Combine(
            Path.GetDirectoryName(outputPath) ?? ".",
            Path.GetFileNameWithoutExtension(outputPath) + "_palette.png");

    private static double ParseDouble(IReadOnlyDictionary<string, string> parameters, string key, double fallback)
    {
        if (parameters.TryGetValue(key, out string? text) &&
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            return value;
        }

        return fallback;
    }

    private static int ParseInt(IReadOnlyDictionary<string, string> parameters, string key, int fallback)
    {
        if (parameters.TryGetValue(key, out string? text) &&
            int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            return value;
        }

        return fallback;
    }

    private sealed class GifProgressAdapter : IProgress<FfmpegProgress>
    {
        private readonly IProgress<JobProgress> _inner;

        public GifProgressAdapter(IProgress<JobProgress> inner) => _inner = inner;

        public void Report(FfmpegProgress value) =>
            _inner.Report(new JobProgress(value.Percentage ?? 0, "正在生成 GIF"));
    }
}
