using TinyPix.Media.Ffmpeg;

namespace TinyPix.Media.Video;

/// <summary>
/// 视频格式转换 (video.convert): re-encodes the source to the chosen container
/// with H.264 video and AAC audio. The encoder and its quality arguments come
/// from <see cref="LgplVideoCodec"/> (OpenH264 target bitrate, VP8/CRF for WebM),
/// never from a hardcoded codec name.
/// Default output name: <c>{原名}_转换.{ext}</c>.
/// </summary>
public sealed class VideoConvertHandler : VideoOutputHandlerBase
{
    public VideoConvertHandler(IFfmpegEngineProvider engine) : base(engine)
    {
    }

    public override string ToolId => "video.convert";

    protected override string OutputSuffix => "_转换";
}
