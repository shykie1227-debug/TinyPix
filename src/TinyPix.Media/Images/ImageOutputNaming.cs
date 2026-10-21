using System.Collections.Generic;
using System.IO;
using ImageMagick;

namespace TinyPix.Media.Images;

/// <summary>
/// Output path rules for the image workbench, mirroring the frozen
/// FEATURE-MATRIX naming (<c>{原名}_缩放.png</c>, <c>{原名}_页_001.jpg</c>,
/// <c>{原名}_联系表_001.jpg</c> …) and the <c> (n)</c> collision rule used by the
/// video workbench. Never writes over an existing file.
/// </summary>
public static class ImageOutputNaming
{
    public static string BuildOutputPath(
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

    /// <summary>
    /// Sequence rule for multi-output tools (多页 TIFF 转换、拆分、联系表分页):
    /// <c>{stem}{suffix}_{index:D3}.{extension}</c> with the same collision guard.
    /// </summary>
    public static string BuildSequenceOutputPath(
        string inputPath,
        string suffix,
        int index,
        string extension,
        string outputDirectory) =>
        BuildOutputPath(inputPath, $"{suffix}_{index:D3}", extension, outputDirectory);

    /// <summary>
    /// Extension used for a tool that keeps the source format: the original
    /// extension, canonicalised through <see cref="ImageFormats.ExtensionFor"/>.
    /// </summary>
    public static string SourceExtension(string inputPath)
    {
        string extension = ImageFormats.ExtensionOf(inputPath);
        return ImageFormats.OutputFormats.ContainsKey(extension) ? extension : "png";
    }

    /// <summary>First-input stem for collage canvases (frozen matrix rule).</summary>
    public static string FirstStem(IReadOnlyList<string> inputPaths) =>
        Path.GetFileNameWithoutExtension(inputPaths[0]);
}
