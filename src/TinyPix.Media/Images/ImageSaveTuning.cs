using System;
using System.Globalization;
using ImageMagick;
using TinyPix.Core.Jobs;

namespace TinyPix.Media.Images;

/// <summary>
/// Per-format encoder tuning for the image workbench. The frozen matrix pins the
/// defaults: JPEG/WebP 质量 90 (转换) / 82 (压缩), 4:2:0, sRGB; PNG
/// CompressionLevel=9 且无损; 像素不变; 保留透明 (PNG/WebP/TIFF/GIF)。
/// </summary>
public static class ImageSaveTuning
{
    /// <summary>Applies quality/compression defines right before writing.</summary>
    public static void Apply(MagickImage image, MagickFormat format, JobRequest request, int defaultQuality = 90)
    {
        int quality = defaultQuality;
        if (TryQuality(request, out int requested))
        {
            quality = requested;
        }

        switch (format)
        {
            case MagickFormat.Jpeg:
                image.Format = MagickFormat.Jpeg;
                image.Quality = (uint)Math.Clamp(quality, 1, 100);
                // 4:2:0 chroma subsampling per the frozen matrix.
                image.Settings.SetDefine(MagickFormat.Jpeg, "sampling-factor", "2x2");
                break;

            case MagickFormat.WebP:
                image.Format = MagickFormat.WebP;
                image.Quality = (uint)Math.Clamp(quality, 1, 100);
                break;

            case MagickFormat.Png:
                image.Format = MagickFormat.Png;
                // 无损：CompressionLevel=9，像素与透明通道不变。
                image.Settings.SetDefine(MagickFormat.Png, "compression-level", "9");
                break;

            default:
                image.Format = format;
                break;
        }
    }

    private static bool TryQuality(JobRequest request, out int quality)
    {
        quality = 0;
        if (request.Parameters.TryGetValue("quality", out string? text) &&
            int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            quality = Math.Clamp(parsed, 1, 100);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Background colour used when flattening transparency into a format without
    /// alpha. Defaults to white per the frozen matrix; accepts
    /// <c>#RRGGBB</c> / <c>#AARRGGBB</c> / named colours.
    /// </summary>
    public static MagickColor ResolveBackground(JobRequest request)
    {
        if (request.Parameters.TryGetValue("background", out string? text) &&
            !string.IsNullOrWhiteSpace(text))
        {
            try
            {
                return new MagickColor(text.Trim());
            }
            catch (MagickMissingDelegateErrorException)
            {
                // Unknown colour name falls through to white.
            }
            catch (ArgumentException)
            {
                // Malformed hex falls through to white.
            }
        }

        return MagickColors.White;
    }
}
