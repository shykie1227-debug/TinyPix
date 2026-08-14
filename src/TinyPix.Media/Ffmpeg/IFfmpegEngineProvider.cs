namespace TinyPix.Media.Ffmpeg;

/// <summary>
/// Resolves the locally bundled, hash-pinned FFmpeg executable path. The concrete
/// implementation lives in the App composition root (it reads the portable engine
/// directory) so Media stays independent of Infrastructure.
/// </summary>
public interface IFfmpegEngineProvider
{
    string FfmpegExecutablePath { get; }
}
