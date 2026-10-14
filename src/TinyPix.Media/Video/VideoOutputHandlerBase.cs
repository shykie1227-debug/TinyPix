using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TinyPix.Core.Jobs;
using TinyPix.Core.Tools;
using TinyPix.Media.Ffmpeg;

namespace TinyPix.Media.Video;

/// <summary>
/// Shared FFmpeg-driven implementation for the 视频输出 workbench handlers
/// (video.convert / video.compress). Builds container-appropriate encode arguments
/// from the request parameters, maps the quality slider to a libx264 CRF, resolves
/// the locally bundled FFmpeg engine, and maps progress back to JobProgress.
/// </summary>
public abstract class VideoOutputHandlerBase : IToolHandler
{
    private static readonly HashSet<string> AudioOnlyFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp3", "wav", "aac", "flac",
    };

    private readonly IFfmpegEngineProvider _engine;

    protected VideoOutputHandlerBase(IFfmpegEngineProvider engine) => _engine = engine;

    /// <summary>Resolved local FFmpeg engine, exposed for subclass encode paths.</summary>
    protected IFfmpegEngineProvider Engine => _engine;

    public abstract string ToolId { get; }

    protected abstract string OutputSuffix { get; }

    public virtual async ValueTask<ToolValidationResult> ValidateAsync(
        JobRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.Parameters.TryGetValue("format", out string? format) ||
            !VideoOutputNaming.IsSupportedFormat(format))
        {
            return ToolValidationResult.Invalid(new JobError(
                "video.format.unsupported", "未指定受支持的输出格式。", false));
        }

        if (request.Parameters.TryGetValue("quality", out string? qualityText) &&
            (!int.TryParse(qualityText, out int quality) || quality < 0 || quality > 100))
        {
            return ToolValidationResult.Invalid(new JobError(
                "video.quality.out-of-range", "质量参数必须在 0–100 之间。", false));
        }

        foreach (string input in request.InputPaths)
        {
            if (!File.Exists(input))
            {
                return ToolValidationResult.Invalid(new JobError(
                    "video.input.missing", $"输入文件不存在：{input}", false));
            }
        }

        return ToolValidationResult.Valid();
    }

    public ValueTask<ToolEstimate> EstimateAsync(
        JobRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(new ToolEstimate(null, null));

    public virtual async Task<ToolExecutionResult> ExecuteAsync(
        JobExecutionContext context,
        CancellationToken cancellationToken)
    {
        string enginePath = _engine.FfmpegExecutablePath;
        if (string.IsNullOrWhiteSpace(enginePath) || !File.Exists(enginePath))
        {
            throw new InvalidOperationException($"无法定位本地媒体引擎：{enginePath ?? "(空)"}");
        }

        var outputs = new List<string>();
        foreach (string input in context.Request.InputPaths)
        {
            string outputPath = VideoOutputNaming.BuildOutputPath(
                input, OutputSuffix, context.Request.Parameters["format"], context.Request.OutputDirectory);
            IReadOnlyList<string> arguments = BuildArguments(input, outputPath, context.Request.Parameters);

            var progress = new JobProgressAdapter(context.Progress);
            FfmpegProcessResult result = await new FfmpegProcessRunner().RunAsync(
                enginePath, arguments, duration: null, progress, cancellationToken).ConfigureAwait(false);

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"媒体引擎返回非零退出码 {result.ExitCode}。{Truncate(result.StandardError)}");
            }

            outputs.Add(outputPath);
        }

        return ToolExecutionResult.Success(outputs);
    }

    /// <summary>
    /// Preview the FFmpeg argument list for a single input. Used by the UI and by
    /// unit tests; does not start the engine.
    /// </summary>
    public IReadOnlyList<string> PreviewArguments(
        string input,
        string outputDirectory,
        IReadOnlyDictionary<string, string> parameters)
    {
        string output = VideoOutputNaming.BuildOutputPath(
            input, OutputSuffix, parameters["format"], outputDirectory);
        return BuildArguments(input, output, parameters);
    }

    public virtual string PreviewOutputPath(
        string input,
        string outputDirectory,
        IReadOnlyDictionary<string, string> parameters) =>
        VideoOutputNaming.BuildOutputPath(
            input,
            OutputSuffix,
            parameters.TryGetValue("format", out string? format) ? format : DefaultExtension,
            outputDirectory);

    /// <summary>Extension used when the caller omits the <c>format</c> parameter.</summary>
    protected virtual string DefaultExtension => "mp4";

    protected virtual IReadOnlyList<string> BuildArguments(
        string input,
        string output,
        IReadOnlyDictionary<string, string> parameters)
    {
        int quality = ParseQuality(parameters);
        string format = parameters["format"].ToLowerInvariant();
        bool audioOnly = AudioOnlyFormats.Contains(format);

        var args = new List<string> { "-y", "-i", input };

        if (audioOnly)
        {
            args.Add("-vn");
            args.AddRange(AudioCodecArgs(format));
        }
        else
        {
            args.AddRange(LgplVideoCodec.EncoderArguments(VideoCodecFor(format), ParseQuality(parameters)));
            args.AddRange(AudioCodecArgs(format));
        }

        args.Add(output);
        return args;
    }

    private static int ParseQuality(IReadOnlyDictionary<string, string> parameters)
    {
        if (parameters.TryGetValue("quality", out string? qualityText) &&
            int.TryParse(qualityText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int quality))
        {
            return Math.Clamp(quality, 0, 100);
        }

        return 80;
    }

    private static string VideoCodecFor(string format) => LgplVideoCodec.ForContainer(format);

    protected static IReadOnlyList<string> AudioCodecArgs(string format) => format switch
    {
        "mp3" or "avi" => new[] { "-c:a", "libmp3lame", "-b:a", "160k" },
        "wav" => new[] { "-c:a", "pcm_s16le" },
        "flac" => new[] { "-c:a", "flac" },
        "aac" => new[] { "-c:a", "aac", "-b:a", "160k" },
        "webm" => new[] { "-c:a", "libvorbis", "-b:a", "160k" },
        _ => new[] { "-c:a", "aac", "-b:a", "160k" },
    };

    protected static string Truncate(string text, int max = 2000)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max)
        {
            return text;
        }

        return text[..max] + " …(已截断)";
    }

    private sealed class JobProgressAdapter : IProgress<FfmpegProgress>
    {
        private readonly IProgress<JobProgress> _inner;

        public JobProgressAdapter(IProgress<JobProgress> inner) => _inner = inner;

        public void Report(FfmpegProgress value) =>
            _inner.Report(new JobProgress(value.Percentage ?? 0, "正在处理媒体"));
    }
}
