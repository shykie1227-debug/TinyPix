namespace TinyPix.Media.Ffmpeg;

/// <summary>
/// Resolves the locally bundled, hash-pinned FFmpeg executable path. The concrete
/// implementation lives in the App composition root (it reads the portable engine
/// directory) so Media stays independent of Infrastructure.
/// </summary>
public interface IFfmpegEngineProvider
{
    string FfmpegExecutablePath { get; }

    /// <summary>
    /// ffprobe ships next to ffmpeg in the same pinned engine bundle (BtbN
    /// release). file.metadata's view mode needs it to read container/stream
    /// facts (编码/尺寸/时长) without decoding a single frame.
    /// </summary>
    string FfprobeExecutablePath { get; }
}
