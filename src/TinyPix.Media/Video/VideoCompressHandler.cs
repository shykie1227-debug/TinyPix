using System.Collections.Generic;
using System.Globalization;
using TinyPix.Media.Ffmpeg;

namespace TinyPix.Media.Video;

/// <summary>
/// 视频压缩 (video.compress): targets 60% of the supplied source average video
/// bitrate (clamped 1–20 Mbps) when available, otherwise falls back to the shared
/// CRF mapping. Default output name: <c>{原名}_压缩.{ext}</c>.
/// </summary>
public sealed class VideoCompressHandler : VideoOutputHandlerBase
{
    public VideoCompressHandler(IFfmpegEngineProvider engine) : base(engine)
    {
    }

    public override string ToolId => "video.compress";

    protected override string OutputSuffix => "_压缩";

    protected override IReadOnlyList<string> BuildArguments(
        string input,
        string output,
        IReadOnlyDictionary<string, string> parameters)
    {
        // FEATURE-MATRIX: 视频压缩 targets 60% of the source average video bitrate
        // (1–20 Mbps) when the caller supplies it; otherwise fall back to CRF.
        if (parameters.TryGetValue("sourceBitrateKbps", out string? bitrateText) &&
            int.TryParse(
                bitrateText,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int sourceKbps) &&
            sourceKbps > 0)
        {
            int targetKbps = Math.Clamp((int)Math.Round(sourceKbps * 0.6), 1000, 20000);
            string format = parameters["format"].ToLowerInvariant();
            var args = new List<string>
            {
                "-y", "-i", input,
                "-c:v", "libx264",
                "-b:v", $"{targetKbps}k",
                "-maxrate", $"{targetKbps}k",
                "-bufsize", $"{targetKbps * 2}k",
            };
            args.AddRange(AudioCodecArgs(format));
            args.Add(output);
            return args;
        }

        return base.BuildArguments(input, output, parameters);
    }
}
