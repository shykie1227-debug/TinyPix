using System;
using System.Collections.Generic;
using System.IO;
using ImageMagick;

namespace TinyPix.Media.Images;

/// <summary>
/// Input/output format rules for the image workbench (冻结自
/// design/FEATURE-MATRIX.md §4). Accepts JPG/JPEG/PNG/WebP/BMP/TIFF on input;
/// convert adds GIF and the same set on output. The set is deliberately small:
/// the frozen matrix never promises RAW/HEIC/AVIF support.
/// </summary>
public static class ImageFormats
{
    public static readonly IReadOnlyDictionary<string, MagickFormat> InputFormats =
        new Dictionary<string, MagickFormat>(StringComparer.OrdinalIgnoreCase)
        {
            ["jpg"] = MagickFormat.Jpeg,
            ["jpeg"] = MagickFormat.Jpeg,
            ["png"] = MagickFormat.Png,
            ["webp"] = MagickFormat.WebP,
            ["bmp"] = MagickFormat.Bmp,
            ["tif"] = MagickFormat.Tiff,
            ["tiff"] = MagickFormat.Tiff,
        };

    public static readonly IReadOnlyDictionary<string, MagickFormat> OutputFormats =
        new Dictionary<string, MagickFormat>(StringComparer.OrdinalIgnoreCase)
        {
            ["jpg"] = MagickFormat.Jpeg,
            ["jpeg"] = MagickFormat.Jpeg,
            ["png"] = MagickFormat.Png,
            ["webp"] = MagickFormat.WebP,
            ["bmp"] = MagickFormat.Bmp,
            ["tif"] = MagickFormat.Tiff,
            ["tiff"] = MagickFormat.Tiff,
            ["gif"] = MagickFormat.Gif,
        };

    /// <summary>Maximum total pixels accepted by the frozen matrix (100 MP).</summary>
    public const long MaxTotalPixels = 100_000_000L;

    public static bool IsSupportedInput(string path)
    {
        string extension = Path.GetExtension(path).TrimStart('.');
        return InputFormats.ContainsKey(extension);
    }

    public static bool IsSupportedOutputFormat(string? format) =>
        !string.IsNullOrWhiteSpace(format) && OutputFormats.ContainsKey(format);

    public static MagickFormat ResolveInputFormat(string path) =>
        InputFormats[Path.GetExtension(path).TrimStart('.')];

    public static MagickFormat ResolveOutputFormat(string format) =>
        OutputFormats[format.ToLowerInvariant()];

    /// <summary>
    /// Formats that cannot hold an alpha channel; transparency must be resolved
    /// against an explicit background colour before writing (frozen matrix rule).
    /// </summary>
    public static bool SupportsAlpha(MagickFormat format) => format switch
    {
        MagickFormat.Png or MagickFormat.WebP or MagickFormat.Tiff or MagickFormat.Gif => true,
        _ => false,
    };

    public static bool SupportsAlphaPath(string path) =>
        SupportsAlpha(ResolveInputFormat(path));

    /// <summary>Canonical extension for a format id (jpg → jpg, tiff → tif).</summary>
    public static string ExtensionFor(string format) => format.ToLowerInvariant() switch
    {
        "jpeg" => "jpg",
        "tiff" => "tif",
        _ => format.ToLowerInvariant(),
    };

    /// <summary>Original file extension without the dot, lower-cased.</summary>
    public static string ExtensionOf(string path) =>
        Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
}
