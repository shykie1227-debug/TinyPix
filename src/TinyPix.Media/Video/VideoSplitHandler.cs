using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TinyPix.Core.Jobs;
using TinyPix.Core.Tools;

namespace TinyPix.Media.Video;

/// <summary>
/// 视频分段 (video.split). Splits one source at the requested split points
/// (comma-separated seconds in <c>points</c>) into consecutive stream-copied
/// segments named <c>{原名}_片段_001.{容器}</c> and up.
/// </summary>
public sealed class VideoSplitHandler : VideoEditHandlerBase
{
    public VideoSplitHandler(IFfmpegEngineProvider engine) : base(engine)
    {
    }

    public override string ToolId => "video.split";

    public override async ValueTask<ToolValidationResult> ValidateAsync(
        JobRequest request, CancellationToken cancellationToken)
    {
        ToolValidationResult baseResult = await base.ValidateAsync(request, cancellationToken);
        if (!baseResult.IsValid)
        {
            return baseResult;
        }

        if (!TryParam(request, "points", out string pointsText) ||
            ParsePoints(pointsText).Count == 0)
        {
            return ToolValidationResult.Invalid(new JobError(
                "video.split.no-points", "请提供至少一个分割点（秒，逗号分隔）。", false));
        }

        return ToolValidationResult.Valid();
    }

    protected override IReadOnlyList<(string OutputPath, IReadOnlyList<string> Arguments)> BuildJobs(
        JobRequest request)
    {
        string input = request.InputPaths[0];
        string extension = OriginalExtension(input);
        IReadOnlyList<double> points = ParsePoints(Require(request, "points"));

        var boundaries = new List<double> { 0 };
        boundaries.AddRange(points);

        var jobs = new List<(string, IReadOnlyList<string>)>();
        for (int i = 0; i < boundaries.Count - 1; i++)
        {
            jobs.Add(BuildSegment(input, extension, boundaries[i], boundaries[i + 1], i + 1, request.OutputDirectory));
        }

        // Final segment: from the last boundary to the end of the file (no -to).
        jobs.Add(BuildSegment(input, extension, boundaries[^1], null, boundaries.Count, request.OutputDirectory));
        return jobs;
    }

    private static (string, IReadOnlyList<string>) BuildSegment(
        string input, string extension, double start, double? end, int index, string outputDirectory)
    {
        string suffix = $"_片段_{index:000}";
        string output = ResolveOutput(input, suffix, extension, outputDirectory);
        var args = new List<string> { "-y", "-ss", start.ToString(CultureInfo.InvariantCulture), "-i", input };
        if (end.HasValue)
        {
            args.Add("-to");
            args.Add(end.Value.ToString(CultureInfo.InvariantCulture));
        }

        args.Add("-c");
        args.Add("copy");
        args.Add(output);
        return (output, args);
    }

    private static IReadOnlyList<double> ParsePoints(string text)
    {
        return text
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim())
            .Where(part => double.TryParse(
                part, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && value > 0)
            .OrderBy(value => value)
            .Distinct()
            .ToList();
    }
}
