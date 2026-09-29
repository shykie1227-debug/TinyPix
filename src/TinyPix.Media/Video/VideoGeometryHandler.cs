using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using TinyPix.Core.Jobs;
using TinyPix.Core.Tools;
using TinyPix.Media.Ffmpeg;

namespace TinyPix.Media.Video;

/// <summary>
/// 裁切、缩放与旋转 (video.geometry). Builds a crop/scale/transpose/flip filter
/// chain and re-encodes to MP4/H.264. Output named <c>{原名}_画面调整.mp4</c>.
/// </summary>
public sealed class VideoGeometryHandler : VideoEditHandlerBase
{
    public VideoGeometryHandler(IFfmpegEngineProvider engine) : base(engine)
    {
    }

    public override string ToolId => "video.geometry";

    protected override IReadOnlyList<(string OutputPath, IReadOnlyList<string> Arguments)> BuildJobs(
        JobRequest request)
    {
        string input = request.InputPaths[0];
        string output = ResolveOutput(input, "_画面调整", "mp4", request.OutputDirectory);

        var filters = new List<string>();
        int cropW = ParamInt(request, "cropW", 0);
        int cropH = ParamInt(request, "cropH", 0);
        int cropX = ParamInt(request, "cropX", 0);
        int cropY = ParamInt(request, "cropY", 0);
        if (cropW >= 16 && cropH >= 16)
        {
            filters.Add($"crop={cropW}:{cropH}:{cropX}:{cropY}");
        }

        int scaleW = ParamInt(request, "scaleW", 0);
        if (scaleW > 0)
        {
            filters.Add($"scale={scaleW}:-2:flags=lanczos");
        }

        int rotate = ParamInt(request, "rotate", 0);
        if (rotate == 90)
        {
            filters.Add("transpose=1");
        }
        else if (rotate == 180)
        {
            filters.Add("rotate=PI");
        }
        else if (rotate == 270)
        {
            filters.Add("transpose=2");
        }

        if (TryParam(request, "flipH", out string flipH) && flipH == "1")
        {
            filters.Add("hflip");
        }

        if (TryParam(request, "flipV", out string flipV) && flipV == "1")
        {
            filters.Add("vflip");
        }

        var args = new List<string> { "-y", "-i", input };
        if (filters.Count > 0)
        {
            args.Add("-vf");
            args.Add(string.Join(",", filters));
        }

        args.AddRange(LgplVideoCodec.H264Arguments());
        args.Add("-c:a");
        args.Add("aac");
        args.Add(output);
        return new[] { (output, (IReadOnlyList<string>)args) };
    }
}
