using System;
using System.IO;
using TinyPix.Infrastructure.Portable;
using TinyPix.Media.Ffmpeg;

namespace TinyPix.App.Services;

/// <summary>
/// Resolves the FFmpeg executable from the portable engine directory
/// (&lt;root&gt;/Engines/ffmpeg[.exe]). The engine is bundled and hash-pinned by the
/// publish pipeline; this provider only locates it at runtime.
/// </summary>
public sealed class PortableFfmpegEngineProvider : IFfmpegEngineProvider
{
    private readonly PortableWorkspace _workspace;

    public PortableFfmpegEngineProvider(PortableWorkspace workspace) => _workspace = workspace;

    public string FfmpegExecutablePath
    {
        get
        {
            string fileName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
            return Path.Combine(_workspace.EnginesDirectory, fileName);
        }
    }

    /// <summary>
    /// ffprobe.exe ships in the same BtbN bundle as ffmpeg.exe (both hash-pinned
    /// by the publish pipeline). Only the file name differs.
    /// </summary>
    public string FfprobeExecutablePath
    {
        get
        {
            string fileName = OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";
            return Path.Combine(_workspace.EnginesDirectory, fileName);
        }
    }
}
