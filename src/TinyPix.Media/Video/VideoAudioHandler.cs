using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TinyPix.Core.Jobs;
using TinyPix.Core.Tools;

namespace TinyPix.Media.Video;

/// <summary>
/// 音频处理 (video.audio). Three modes selected by <c>mode</c>:
/// <list type="bullet">
///   <item><description>extract — 提取音轨为 MP3 192 kbps（<c>{原名}_音频.mp3</c>）</description></item>
///   <item><description>mute — 移除音轨（<c>{原名}_静音.mp4</c>）</description></item>
///   <item><description>replace — 用第二个输入替换音轨（<c>{原名}_替换音频.mp4</c>）</description></item>
/// </list>
/// </summary>
public sealed class VideoAudioHandler : VideoEditHandlerBase
{
    public VideoAudioHandler(IFfmpegEngineProvider engine) : base(engine)
    {
    }

    public override string ToolId => "video.audio";

    public override async ValueTask<ToolValidationResult> ValidateAsync(
        JobRequest request, CancellationToken cancellationToken)
    {
        ToolValidationResult baseResult = await base.ValidateAsync(request, cancellationToken);
        if (!baseResult.IsValid)
        {
            return baseResult;
        }

        string mode = TryParam(request, "mode", out string m) ? m : "extract";
        if (string.Equals(mode, "replace", StringComparison.OrdinalIgnoreCase) &&
            request.InputPaths.Count < 2)
        {
            return ToolValidationResult.Invalid(new JobError(
                "video.audio.replace.needs-audio", "替换音频需要提供视频与音频两个输入。", false));
        }

        return ToolValidationResult.Valid();
    }

    protected override IReadOnlyList<(string OutputPath, IReadOnlyList<string> Arguments)> BuildJobs(
        JobRequest request)
    {
        string mode = TryParam(request, "mode", out string m) ? m : "extract";
        string input = request.InputPaths[0];

        if (string.Equals(mode, "mute", StringComparison.OrdinalIgnoreCase))
        {
            string muteOut = ResolveOutput(input, "_静音", "mp4", request.OutputDirectory);
            var muteArgs = new List<string> { "-y", "-i", input, "-an", "-c:v", "copy", muteOut };
            return new[] { (muteOut, muteArgs) };
        }

        if (string.Equals(mode, "replace", StringComparison.OrdinalIgnoreCase))
        {
            string audio = request.InputPaths[1];
            string replaceOut = ResolveOutput(input, "_替换音频", "mp4", request.OutputDirectory);
            var replaceArgs = new List<string>
            {
                "-y", "-i", input, "-i", audio,
                "-map", "0:v:0", "-map", "1:a:0",
                "-c:v", "copy", "-c:a", "aac", "-shortest",
                replaceOut,
            };
            return new[] { (replaceOut, replaceArgs) };
        }

        // extract (default)
        string extractOut = ResolveOutput(input, "_音频", "mp3", request.OutputDirectory);
        var extractArgs = new List<string>
        {
            "-y", "-i", input, "-vn", "-c:a", "libmp3lame", "-b:a", "192k", extractOut,
        };
        return new[] { (extractOut, extractArgs) };
    }
}
