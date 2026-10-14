using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TinyPix.Core.Jobs;
using TinyPix.Core.Tools;
using TinyPix.Media.Ffmpeg;

namespace TinyPix.Media.Video;

/// <summary>
/// 速度与音量 (video.speed-volume). Applies setpts video retiming and an atempo
/// (plus optional volume) audio chain, re-encoding to MP4/H.264. Output named
/// <c>{原名}_速度音量.mp4</c>.
/// </summary>
public sealed class VideoSpeedVolumeHandler : VideoEditHandlerBase
{
    public VideoSpeedVolumeHandler(IFfmpegEngineProvider engine) : base(engine)
    {
    }

    public override string ToolId => "video.speed-volume";

    public override async ValueTask<ToolValidationResult> ValidateAsync(
        JobRequest request, CancellationToken cancellationToken)
    {
        ToolValidationResult baseResult = await base.ValidateAsync(request, cancellationToken);
        if (!baseResult.IsValid)
        {
            return baseResult;
        }

        double speed = ParamDouble(request, "speed", 1);
        int volume = ParamInt(request, "volume", 100);
        if (speed < 0.25 || speed > 4.0)
        {
            return ToolValidationResult.Invalid(new JobError(
                "video.speed-volume.speed-out-of-range", "速度必须在 0.25–4.00 之间。", false));
        }

        if (volume < 0 || volume > 200)
        {
            return ToolValidationResult.Invalid(new JobError(
                "video.speed-volume.volume-out-of-range", "音量必须在 0–200% 之间。", false));
        }

        return ToolValidationResult.Valid();
    }

    protected override IReadOnlyList<(string OutputPath, IReadOnlyList<string> Arguments)> BuildJobs(
        JobRequest request)
    {
        string input = request.InputPaths[0];
        string output = ResolveOutput(input, "_速度音量", "mp4", request.OutputDirectory);

        double speed = ParamDouble(request, "speed", 1);
        int volume = ParamInt(request, "volume", 100);

        string speedExpr = speed.ToString("F3", CultureInfo.InvariantCulture);
        IReadOnlyList<string> atempo = AtempoChain(speed);
        string volumeExpr = (volume / 100.0).ToString("F3", CultureInfo.InvariantCulture);

        var audioChain = new List<string>();
        audioChain.AddRange(atempo);
        audioChain.Add($"volume={volumeExpr}");

        string filter =
            $"[0:v]setpts=PTS/{speedExpr}[v];[0:a]{string.Join(",", audioChain)}[a]";

        var args = new List<string>
        {
            "-y", "-i", input,
            "-filter_complex", filter,
            "-map", "[v]", "-map", "[a]",
        };
        args.AddRange(LgplVideoCodec.H264Arguments());
        args.Add("-c:a");
        args.Add("aac");
        args.Add(output);
        return new[] { (output, (IReadOnlyList<string>)args) };
    }
}
