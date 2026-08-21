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
/// 视频时间剪辑 (video.trim). Default is keyframe-priority stream copy; the
/// "precise" mode re-encodes with H.264/AAC and clamps the output to MP4.
/// </summary>
public sealed class VideoTrimHandler : VideoEditHandlerBase
{
    public VideoTrimHandler(IFfmpegEngineProvider engine) : base(engine)
    {
    }

    public override string ToolId => "video.trim";

    public override async ValueTask<ToolValidationResult> ValidateAsync(
        JobRequest request, CancellationToken cancellationToken)
    {
        ToolValidationResult baseResult = await base.ValidateAsync(request, cancellationToken);
        if (!baseResult.IsValid)
        {
            return baseResult;
        }

        double start = ParamDouble(request, "startSecs", 0);
        double end = ParamDouble(request, "endSecs", 0);
        if (end - start < 0.1)
        {
            return ToolValidationResult.Invalid(new JobError(
                "video.trim.too-short", "剪辑区间需至少 0.1 秒。", false));
        }

        return ToolValidationResult.Valid();
    }

    protected override IReadOnlyList<(string OutputPath, IReadOnlyList<string> Arguments)> BuildJobs(
        JobRequest request)
    {
        string input = request.InputPaths[0];
        double start = ParamDouble(request, "startSecs", 0);
        double end = ParamDouble(request, "endSecs", 0);
        bool precise = TryParam(request, "precise", out string preciseText) && preciseText == "1";
        string extension = precise ? "mp4" : OriginalExtension(input);
        string output = ResolveOutput(input, "_剪辑", extension, request.OutputDirectory);

        var args = new List<string> { "-y" };
        if (!precise)
        {
            args.Add("-ss");
            args.Add(start.ToString(CultureInfo.InvariantCulture));
        }

        args.Add("-i");
        args.Add(input);

        if (!precise)
        {
            args.Add("-to");
            args.Add(end.ToString(CultureInfo.InvariantCulture));
            args.Add("-c");
            args.Add("copy");
        }
        else
        {
            args.Add("-ss");
            args.Add(start.ToString(CultureInfo.InvariantCulture));
            args.Add("-to");
            args.Add(end.ToString(CultureInfo.InvariantCulture));
            args.Add("-c:v");
            args.Add("libx264");
            args.Add("-crf");
            args.Add("20");
            args.Add("-c:a");
            args.Add("aac");
        }

        args.Add(output);
        return new[] { (output, args) };
    }
}
