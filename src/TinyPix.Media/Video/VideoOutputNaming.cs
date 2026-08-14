using System.Collections.Generic;
using System.IO;

namespace TinyPix.Media.Video;

/// <summary>
/// Builds the default output path for a video-output job, matching the frozen
/// FEATURE-MATRIX naming rules (e.g. <c>{原名}_转换.mp4</c>,
/// <c>{原名}_压缩.mp4</c>) and the <c> (n)</c> collision rule.
/// </summary>
public static class VideoOutputNaming
{
    private static readonly IReadOnlyDictionary<string, string> FormatExtension =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["mp4"] = "mp4",
            ["mov"] = "mov",
            ["mkv"] = "mkv",
            ["avi"] = "avi",
            ["webm"] = "webm",
            ["mp3"] = "mp3",
            ["wav"] = "wav",
            ["aac"] = "aac",
            ["flac"] = "flac",
        };

    public static bool IsSupportedFormat(string? format) =>
        !string.IsNullOrWhiteSpace(format) && FormatExtension.ContainsKey(format!);

    public static string ContainerExtension(string format) => FormatExtension[format];

    public static string BuildOutputPath(
        string inputPath,
        string suffix,
        string format,
        string outputDirectory)
    {
        string stem = Path.GetFileNameWithoutExtension(inputPath);
        string extension = ContainerExtension(format);
        string candidate = Path.Combine(outputDirectory, $"{stem}{suffix}.{extension}");
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        int index = 1;
        string conflict;
        do
        {
            conflict = Path.Combine(outputDirectory, $"{stem}{suffix} ({index}).{extension}");
            index++;
        }
        while (File.Exists(conflict));

        return conflict;
    }
}
