using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TinyPix.Core.Jobs;
using TinyPix.Core.Tools;
using TinyPix.Media.Ffmpeg;

namespace TinyPix.Media.Video;

/// <summary>
/// Shared FFmpeg-driven base for the 视频编辑 workbench handlers
/// (video.trim / split / merge / geometry / speed-volume / audio / overlay /
/// frames / metadata). Subclasses implement <see cref="BuildJobs"/> to emit one
/// or more (outputPath, ffmpegArgs) pairs from a single <see cref="JobRequest"/>;
/// the base owns engine resolution, execution, progress mapping and preview.
/// </summary>
public abstract class VideoEditHandlerBase : IToolHandler
{
    private readonly IFfmpegEngineProvider _engine;

    protected VideoEditHandlerBase(IFfmpegEngineProvider engine) => _engine = engine;

    /// <summary>Resolved local FFmpeg engine, exposed for subclass encode paths.</summary>
    protected IFfmpegEngineProvider Engine => _engine;

    public abstract string ToolId { get; }

    public virtual async ValueTask<ToolValidationResult> ValidateAsync(
        JobRequest request,
        CancellationToken cancellationToken)
    {
        if (request.InputPaths.Count == 0)
        {
            return ToolValidationResult.Invalid(new JobError(
                "video.input.empty", "至少需要一个输入文件。", false));
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

        IReadOnlyList<(string OutputPath, IReadOnlyList<string> Arguments)> jobs =
            BuildJobs(context.Request);

        var outputs = new List<string>();
        foreach ((string outputPath, IReadOnlyList<string> arguments) in jobs)
        {
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
    /// Compute the (output, arguments) jobs for a request. Pure string building and
    /// must not start the engine; it is also used by the preview helpers.
    /// </summary>
    protected abstract IReadOnlyList<(string OutputPath, IReadOnlyList<string> Arguments)> BuildJobs(
        JobRequest request);

    /// <summary>Preview the expected output paths (UI preflight). Does not run the engine.</summary>
    public virtual IReadOnlyList<string> PreviewOutputPaths(JobRequest request)
    {
        try
        {
            return BuildJobs(request).Select(job => job.OutputPath).ToList();
        }
        catch (Exception)
        {
            return request.InputPaths
                .Select(input => Path.Combine(
                    request.OutputDirectory,
                    Path.GetFileNameWithoutExtension(input) + Path.GetExtension(input)))
                .ToList();
        }
    }

    /// <summary>Preview the FFmpeg argument list for the first job. Used by tests/UI.</summary>
    public virtual IReadOnlyList<string> PreviewArguments(JobRequest request)
    {
        IReadOnlyList<(string OutputPath, IReadOnlyList<string> Arguments)> jobs = BuildJobs(request);
        return jobs.Count > 0 ? jobs[0].Arguments : Array.Empty<string>();
    }

    protected static string ResolveOutput(
        string input, string suffix, string extension, string outputDirectory) =>
        VideoOutputNaming.BuildOutputPathByExtension(input, suffix, extension, outputDirectory);

    protected static string OriginalExtension(string input) =>
        (Path.GetExtension(input)?.TrimStart('.').ToLowerInvariant()) ?? "mp4";

    protected static string Truncate(string text, int max = 2000)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max)
        {
            return text;
        }

        return text[..max] + " …(已截断)";
    }

    protected static bool TryParam(JobRequest request, string key, out string value) =>
        request.Parameters.TryGetValue(key, out value!) && !string.IsNullOrWhiteSpace(value);

    protected static string Require(JobRequest request, string key)
    {
        if (!request.Parameters.TryGetValue(key, out string? value) || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"缺少必需参数：{key}");
        }

        return value;
    }

    protected static double ParamDouble(JobRequest request, string key, double fallback)
    {
        if (TryParam(request, key, out string raw) &&
            double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            return value;
        }

        return fallback;
    }

    protected static int ParamInt(JobRequest request, string key, int fallback)
    {
        if (TryParam(request, key, out string raw) &&
            int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            return value;
        }

        return fallback;
    }

    /// <summary>Build an FFmpeg atempo chain covering arbitrary 0.25–4.0 speed factors.</summary>
    protected static IReadOnlyList<string> AtempoChain(double factor)
    {
        factor = Math.Clamp(factor, 0.25, 4.0);
        var steps = new List<string>();
        while (factor > 2.0 + 1e-6)
        {
            steps.Add("atempo=2.0");
            factor /= 2.0;
        }

        while (factor < 0.5 - 1e-6)
        {
            steps.Add("atempo=0.5");
            factor *= 2.0;
        }

        steps.Add($"atempo={factor.ToString("F3", CultureInfo.InvariantCulture)}");
        return steps;
    }

    private sealed class JobProgressAdapter : IProgress<FfmpegProgress>
    {
        private readonly IProgress<JobProgress> _inner;

        public JobProgressAdapter(IProgress<JobProgress> inner) => _inner = inner;

        public void Report(FfmpegProgress value) =>
            _inner.Report(new JobProgress(value.Percentage ?? 0, "正在处理媒体"));
    }
}
