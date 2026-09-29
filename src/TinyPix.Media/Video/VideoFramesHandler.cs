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
/// 截图与帧提取 (video.frames). Two modes selected by <c>mode</c>:
/// <list type="bullet">
///   <item><description>single — 在 <c>timeSecs</c> 处提取单帧 PNG（<c>{原名}_{时-分-秒-毫秒}.png</c>）</description></item>
///   <item><description>sequence — 从 <c>intervalSecs</c> 起等间隔取 <c>count</c> 张（<c>{原名}_帧_001.png</c>…）</description></item>
/// </list>
/// </summary>
public sealed class VideoFramesHandler : VideoEditHandlerBase
{
    public VideoFramesHandler(IFfmpegEngineProvider engine) : base(engine)
    {
    }

    public override string ToolId => "video.frames";

    protected override IReadOnlyList<(string OutputPath, IReadOnlyList<string> Arguments)> BuildJobs(
        JobRequest request)
    {
        string input = request.InputPaths[0];
        string mode = TryParam(request, "mode", out string m) ? m : "single";
        var jobs = new List<(string, IReadOnlyList<string>)>();

        if (string.Equals(mode, "sequence", StringComparison.OrdinalIgnoreCase))
        {
            int count = ParamInt(request, "count", 10);
            double interval = ParamDouble(request, "intervalSecs", 1);
            for (int i = 1; i <= count; i++)
            {
                double time = i * interval;
                string output = ResolveOutput(input, $"_帧_{i:000}", "png", request.OutputDirectory);
                jobs.Add((output, SingleFrameArgs(input, output, time)));
            }

            return jobs;
        }

        // single (default)
        double at = ParamDouble(request, "timeSecs", 0);
        string stamp = TimestampStamp(at);
        string singleOut = ResolveOutput(input, $"_{stamp}", "png", request.OutputDirectory);
        jobs.Add((singleOut, SingleFrameArgs(input, singleOut, at)));
        return jobs;
    }

    private static IReadOnlyList<string> SingleFrameArgs(string input, string output, double time) =>
        new List<string>
        {
            "-y", "-ss", time.ToString(CultureInfo.InvariantCulture), "-i", input,
            "-frames:v", "1", "-q:v", "2", output,
        };

    private static string TimestampStamp(double seconds)
    {
        int totalMs = (int)Math.Round(seconds * 1000);
        int hh = totalMs / 3_600_000;
        int mm = (totalMs % 3_600_000) / 60_000;
        int ss = (totalMs % 60_000) / 1000;
        int ms = totalMs % 1000;
        return $"{hh:00}-{mm:00}-{ss:00}-{ms:000}";
    }
}
