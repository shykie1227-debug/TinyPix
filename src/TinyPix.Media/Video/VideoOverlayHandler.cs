using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TinyPix.Core.Jobs;
using TinyPix.Core.Tools;

namespace TinyPix.Media.Video;

/// <summary>
/// 水印与字幕 (video.overlay). Two modes selected by <c>mode</c>, each needing a
/// second input (image for watermark, subtitle file for subtitles):
/// <list type="bullet">
///   <item><description>watermark — 右下角叠加图片（<c>{原名}_水印.mp4</c>）</description></item>
///   <item><description>subtitle — 烧录字幕（<c>{原名}_字幕.mp4</c>）</description></item>
/// </list>
/// </summary>
public sealed class VideoOverlayHandler : VideoEditHandlerBase
{
    public VideoOverlayHandler(IFfmpegEngineProvider engine) : base(engine)
    {
    }

    public override string ToolId => "video.overlay";

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
                "video.overlay.needs-overlay", "水印/字幕需要提供视频与叠加文件两个输入。", false));
        }

        return ToolValidationResult.Valid();
    }

    protected override IReadOnlyList<(string OutputPath, IReadOnlyList<string> Arguments)> BuildJobs(
        JobRequest request)
    {
        string video = request.InputPaths[0];
        string overlay = request.InputPaths[1];
        string mode = TryParam(request, "mode", out string m) ? m : "watermark";

        if (string.Equals(mode, "subtitle", StringComparison.OrdinalIgnoreCase))
        {
            string subOut = ResolveOutput(video, "_字幕", "mp4", request.OutputDirectory);
            string subtitleFilter = $"subtitles='{overlay.Replace('\\', '/')}'";
            var subArgs = new List<string>
            {
                "-y", "-i", video,
                "-vf", subtitleFilter,
                "-c:v", "libx264", "-crf", "20",
                "-c:a", "copy",
                subOut,
            };
            return new[] { (subOut, subArgs) };
        }

        // watermark (default)
        string wmOut = ResolveOutput(video, "_水印", "mp4", request.OutputDirectory);
        var wmArgs = new List<string>
        {
            "-y", "-i", video, "-i", overlay,
            "-filter_complex", "[0][1]overlay=W-w-10:H-h-10[out]",
            "-map", "[out]", "-map", "0:a?",
            "-c:v", "libx264", "-crf", "20",
            "-c:a", "aac",
            wmOut,
        };
        return new[] { (wmOut, wmArgs) };
    }
}
