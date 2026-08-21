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
/// 视频合并 (video.merge). Concatenates two or more inputs (in the supplied
/// order) into a single MP4/H.264 using the FFmpeg concat filter. Output is
/// named <c>{首个原名}_合并.mp4</c>.
/// </summary>
public sealed class VideoMergeHandler : VideoEditHandlerBase
{
    public VideoMergeHandler(IFfmpegEngineProvider engine) : base(engine)
    {
    }

    public override string ToolId => "video.merge";

    public override async ValueTask<ToolValidationResult> ValidateAsync(
        JobRequest request, CancellationToken cancellationToken)
    {
        ToolValidationResult baseResult = await base.ValidateAsync(request, cancellationToken);
        if (!baseResult.IsValid)
        {
            return baseResult;
        }

        if (request.InputPaths.Count < 2)
        {
            return ToolValidationResult.Invalid(new JobError(
                "video.merge.too-few", "合并至少需要 2 个输入。", false));
        }

        return ToolValidationResult.Valid();
    }

    protected override IReadOnlyList<(string OutputPath, IReadOnlyList<string> Arguments)> BuildJobs(
        JobRequest request)
    {
        int count = request.InputPaths.Count;
        string first = request.InputPaths[0];
        string output = ResolveOutput(first, "_合并", "mp4", request.OutputDirectory);

        var args = new List<string> { "-y" };
        foreach (string input in request.InputPaths)
        {
            args.Add("-i");
            args.Add(input);
        }

        args.Add("-filter_complex");
        args.Add($"concat=n={count}:v=1:a=1[outv][outa]");
        args.Add("-map");
        args.Add("[outv]");
        args.Add("-map");
        args.Add("[outa]");
        args.Add("-vsync");
        args.Add("vfr");
        args.Add("-pix_fmt");
        args.Add("yuv420p");
        args.Add("-movflags");
        args.Add("+faststart");
        args.Add(output);
        return new[] { (output, args) };
    }
}
