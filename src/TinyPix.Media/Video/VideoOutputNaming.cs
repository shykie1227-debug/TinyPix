using System.Collections.Generic;
using System.IO;

namespace TinyPix.Media.Video;

/// <summary>
/// Builds the default output path for a media-output job, matching the frozen
/// FEATURE-MATRIX naming rules (e.g. <c>{原名}_转换.mp4</c>,
/// <c>{原名}_压缩.mp4</c>, <c>{原名}_GIF.gif</c>) and the <c> (n)</c> collision rule.
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
            ["gif"] = "gif",
        };

    public static bool IsSupportedFormat(string? format) =>
        !string.IsNullOrWhiteSpace(format) && FormatExtension.ContainsKey(format!);

    public static string ContainerExtension(string format) => FormatExtension[format];

    /// <summary>
    /// Build an output path using an explicit extension (e.g. <c>m4v</c>, <c>ts</c>,
    /// <c>png</c>) that need not be in the supported-format map. Used by the video
    /// edit handlers whose outputs keep the source container or become images.
    /// </summary>
    public static string BuildOutputPathByExtension(
        string inputPath,
        string suffix,
        string extension,
        string outputDirectory)
    {
        string stem = Path.GetFileNameWithoutExtension(inputPath);
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
