using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TinyPix.Core.Jobs;
using TinyPix.Core.Tools;

namespace TinyPix.Media.Video;

/// <summary>
/// 媒体信息与元数据清理 (video.metadata). Two modes selected by <c>mode</c>:
/// <list type="bullet">
///   <item><description>view — 仅查看，不产生输出文件</description></item>
///   <item><description>clean — 复制流并剥离元数据（<c>{原名}_已清理.{原扩展名}</c>）</description></item>
/// </list>
/// </summary>
public sealed class VideoMetadataHandler : VideoEditHandlerBase
{
    public VideoMetadataHandler(IFfmpegEngineProvider engine) : base(engine)
    {
    }

    public override string ToolId => "video.metadata";

    protected override IReadOnlyList<(string OutputPath, IReadOnlyList<string> Arguments)> BuildJobs(
        JobRequest request)
    {
        string mode = TryParam(request, "mode", out string m) ? m : "view";
        if (string.Equals(mode, "view", StringComparison.OrdinalIgnoreCase))
        {
            // Viewing produces no output file; the shell inspects via ffprobe/metadata.
            return Array.Empty<(string, IReadOnlyList<string>)>();
        }

        string input = request.InputPaths[0];
        string extension = OriginalExtension(input);
        string output = ResolveOutput(input, "_已清理", extension, request.OutputDirectory);
        var args = new List<string> { "-y", "-i", input, "-map_metadata", "-1", "-c", "copy", output };
        return new[] { (output, args) };
    }
}
