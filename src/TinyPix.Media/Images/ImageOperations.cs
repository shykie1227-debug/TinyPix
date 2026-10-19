using System;
using System.Collections.Generic;
using System.Globalization;
using ImageMagick;
using ImageMagick.Drawing;
using TinyPix.Core.Jobs;

namespace TinyPix.Media.Images;

/// <summary>
/// Pixel operations shared by the individual image handlers and by
/// image.batch (whose frozen step order is 方向归一 → 裁切 → 缩放 → 调色 → 水印
/// → 格式/压缩). Every method is pure: it mutates the passed MagickImage and
/// reports nothing, so both single-tool and batch paths stay identical.
/// </summary>
public static class ImageOperations
{
    // ---- crop ---------------------------------------------------------------

    public readonly record struct CropRect(int X, int Y, int Width, int Height);

    /// <summary>
    /// Resolves the crop rectangle: an explicit x/y/width/height wins; otherwise
    /// the largest rect of <c>ratio</c> (e.g. 1:1, 4:3, 3:2, 16:9) centred in the
    /// image. Always clamped into the image bounds.
    /// </summary>
    public static CropRect ResolveCrop(
        MagickImage image,
        int? x,
        int? y,
        int? width,
        int? height,
        double? ratio)
    {
        int imageWidth = (int)image.Width;
        int imageHeight = (int)image.Height;

        if (width is > 0 && height is > 0)
        {
            int left = Math.Clamp(x ?? 0, 0, Math.Max(0, imageWidth - 1));
            int top = Math.Clamp(y ?? 0, 0, Math.Max(0, imageHeight - 1));
            int right = Math.Min(left + width.Value, imageWidth);
            int bottom = Math.Min(top + height.Value, imageHeight);
            return new CropRect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
        }

        if (ratio is > 0)
        {
            // Largest ratio-fitting rect, centred.
            double byWidth = imageWidth / ratio.Value;
            int rectWidth;
            int rectHeight;
            if (byWidth <= imageHeight)
            {
                rectWidth = imageWidth;
                rectHeight = Math.Max(1, (int)Math.Round(byWidth));
            }
            else
            {
                rectHeight = imageHeight;
                rectWidth = Math.Max(1, (int)Math.Round(imageHeight * ratio.Value));
            }

            return new CropRect(
                (imageWidth - rectWidth) / 2,
                (imageHeight - rectHeight) / 2,
                rectWidth,
                rectHeight);
        }

        return new CropRect(0, 0, imageWidth, imageHeight);
    }

    public static void Crop(MagickImage image, CropRect rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            throw new ArgumentException("裁切区域必须大于 0。");
        }

        image.Crop(new MagickGeometry(rect.X, rect.Y, (uint)rect.Width, (uint)rect.Height));
    }

    public static double? ParseRatio(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string[] parts = text.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double a) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double b) &&
            a > 0 && b > 0)
        {
            return a / b;
        }

        return null;
    }

    // ---- resize -------------------------------------------------------------

    /// <summary>
    /// Resolves the target pixel size honouring 锁定比例 / 不放大. Percentage mode
    /// scales both axes; explicit width/height acts as a bounding box when the
    /// aspect is locked, or as an exact size when it is not.
    /// </summary>
    public static (int Width, int Height) ResolveTargetSize(
        MagickImage image,
        int? width,
        int? height,
        double? percentage,
        bool keepAspect,
        bool noEnlarge)
    {
        int sourceWidth = (int)image.Width;
        int sourceHeight = (int)image.Height;

        int targetWidth;
        int targetHeight;

        if (percentage is > 0)
        {
            targetWidth = Math.Max(1, (int)Math.Round(sourceWidth * percentage.Value / 100.0));
            targetHeight = Math.Max(1, (int)Math.Round(sourceHeight * percentage.Value / 100.0));
        }
        else if (width is > 0 && height is > 0 && keepAspect)
        {
            double scale = Math.Min(width.Value / (double)sourceWidth, height.Value / (double)sourceHeight);
            targetWidth = Math.Max(1, (int)Math.Round(sourceWidth * scale));
            targetHeight = Math.Max(1, (int)Math.Round(sourceHeight * scale));
        }
        else if (width is > 0)
        {
            targetWidth = width.Value;
            targetHeight = keepAspect
                ? Math.Max(1, (int)Math.Round(sourceHeight * (targetWidth / (double)sourceWidth)))
                : sourceHeight;
        }
        else if (height is > 0)
        {
            targetHeight = height.Value;
            targetWidth = keepAspect
                ? Math.Max(1, (int)Math.Round(sourceWidth * (targetHeight / (double)sourceHeight)))
                : sourceWidth;
        }
        else
        {
            return (sourceWidth, sourceHeight);
        }

        if (noEnlarge)
        {
            if (targetWidth >= sourceWidth && targetHeight >= sourceHeight)
            {
                return (sourceWidth, sourceHeight);
            }

            double clamp = Math.Min(1.0, Math.Min(
                targetWidth / (double)sourceWidth, targetHeight / (double)sourceHeight));
            targetWidth = Math.Max(1, (int)Math.Round(sourceWidth * clamp));
            targetHeight = Math.Max(1, (int)Math.Round(sourceHeight * clamp));
        }

        return (targetWidth, targetHeight);
    }

    public static void Resize(MagickImage image, int width, int height, string? filterName)
    {
        image.FilterType = ResizeFilter(filterName);
        image.Resize((uint)width, (uint)height);
    }

    /// <summary>Lanczos is the frozen default (“高质量”); Mitchell for 标准.</summary>
    public static FilterType ResizeFilter(string? name) => name?.ToLowerInvariant() switch
    {
        "mitchell" or "standard" => FilterType.Mitchell,
        "catrom" => FilterType.Catrom,
        "triangle" or "bilinear" => FilterType.Triangle,
        "box" => FilterType.Box,
        _ => FilterType.Lanczos,
    };

    // ---- colour -------------------------------------------------------------

    public static void ApplyColor(
        MagickImage image,
        int brightness,
        int contrast,
        int saturation,
        int temperature,
        int sharpen)
    {
        if (brightness != 0 || contrast != 0)
        {
            image.BrightnessContrast(
                new Percentage(brightness),
                new Percentage(contrast),
                Channels.Composite);
        }

        if (saturation != 0)
        {
            image.Modulate(
                new Percentage(100),
                new Percentage(100 + saturation),
                new Percentage(100));
        }

        if (temperature != 0)
        {
            // Positive = warmer (red up, blue down), negative = cooler.
            double shift = temperature / 200.0; // ±50% at the extremes
            image.Evaluate(Channels.Red, EvaluateOperator.Multiply, 1.0 + shift);
            image.Evaluate(Channels.Blue, EvaluateOperator.Multiply, 1.0 - shift);
        }

        if (sharpen > 0)
        {
            double amount = sharpen / 50.0; // 0–100 → 0–2.0
            image.UnsharpMask(2.0, 1.0, amount, 0.05);
        }
    }

    /// <summary>
    /// 自动增强 (default OFF): AutoLevel + ContrastStretch(0.5%, 0.5%) per the
    /// frozen matrix. Explicitly not an AI enhance.
    /// </summary>
    public static void ApplyAutoEnhance(MagickImage image)
    {
        image.AutoLevel();
        image.ContrastStretch(new Percentage(0.5), new Percentage(0.5));
    }

    // ---- watermark ----------------------------------------------------------

    /// <summary>
    /// Font that actually renders the text. "Segoe UI" has no CJK glyphs and
    /// ImageMagick on Windows silently draws NOTHING for pure Chinese text
    /// (verified 2026-10-16 on Win10 22H2: distortion 0 for 机密 with Segoe UI,
    /// Microsoft YaHei, SimSun and MS Gothic; DengXian renders it). Latin text
    /// keeps Segoe UI; anything above the CJK block switches to the first
    /// candidate that demonstrably draws, cached per process.
    /// </summary>
    private static readonly string[] CjkFontCandidates =
    {
        "DengXian", "Microsoft YaHei", "SimSun", "MS Gothic", "Yu Gothic", "Segoe UI",
    };

    private static string? s_cjkFont;

    private static bool NeedsCjkFont(string text)
    {
        foreach (char c in text)
        {
            // CJK radicals / kana / hangul / fullwidth forms and above.
            if (c > '\u2E7F')
            {
                return true;
            }
        }

        return false;
    }

    private static string ResolveTextFont(MagickImage image, string text)
    {
        if (!NeedsCjkFont(text))
        {
            return "Segoe UI";
        }

        if (s_cjkFont is not null)
        {
            return s_cjkFont;
        }

        foreach (string candidate in CjkFontCandidates)
        {
            if (RendersText(candidate, text))
            {
                s_cjkFont = candidate;
                return candidate;
            }
        }

        return "Segoe UI";
    }

    /// <summary>
    /// Draws the text with the candidate font on a scratch canvas and checks the
    /// pixels actually changed - FontTypeMetrics returns metrics even for fonts
    /// that render nothing, so the draw is the only honest probe.
    /// </summary>
    private static bool RendersText(string font, string text)
    {
        try
        {
            using var canvas = new MagickImage(MagickColors.White, 128, 64);
            using var probe = canvas.Clone();
            probe.Settings.Font = font;
            probe.Settings.FontPointsize = 24;
            probe.Settings.FillColor = MagickColors.Black;
            probe.Draw(new Drawables().Font(font).FontPointSize(24).FillColor(MagickColors.Black).Text(4, 40, text));
            double distortion = canvas.Compare(probe, ErrorMetric.RootMeanSquared);
            return distortion > 0.0001;
        }
        catch
        {
            return false;
        }
    }

    public static void ApplyTextWatermark(
        MagickImage image,
        JobRequest request,
        string text,
        string position,
        int opacityPercent,
        int tile)
    {
        int shortSide = (int)Math.Min(image.Width, image.Height);
        double sizePercent = 4.0;
        if (request.Parameters.TryGetValue("sizePercent", out string? sizeText) &&
            double.TryParse(sizeText!, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) &&
            parsed > 0)
        {
            sizePercent = Math.Clamp(parsed, 1, 50);
        }

        double fontSize = Math.Clamp(shortSide * sizePercent / 100.0, 16, 192);
        int margin = Math.Max(4, (int)Math.Round(shortSide * 0.03));
        // Q16 quantum alpha: MagickColor's (r,g,b,a) overload takes quantum values
        // (0-65535), NOT 8-bit bytes. Passing a 0-255 byte renders the watermark
        // at ~0.3% opacity (regression found by the VM probe).
        ushort alpha = AlphaQuantum(opacityPercent);

        MagickColor fill = ResolveWatermarkColor(request, opacityPercent);
        string font = ResolveTextFont(image, text);

        image.Settings.Font = font;
        image.Settings.FontPointsize = fontSize;
        image.Settings.FillColor = fill;
        image.Settings.StrokeColor = new MagickColor(0, 0, 0, alpha);

        ITypeMetric? metrics = image.FontTypeMetrics(text);
        if (metrics is null)
        {
            return; // no font available: skip the text watermark rather than failing
        }

        // Frozen matrix: 白字 2 px 黑描边 — a constant 2px stroke, not a
        // font-size fraction (the old fontSize/12 made 192pt text carry a 16px
        // stroke that swallowed thin glyphs entirely).
        int stroke = 2;
        int textWidth = (int)Math.Ceiling(metrics.TextWidth) + stroke * 2;
        int textHeight = (int)Math.Ceiling(metrics.Ascent + metrics.Descent) + stroke * 2;

        if (tile > 0)
        {
            int stepX = textWidth + margin * 2;
            int stepY = textHeight + margin * 2;
            for (int row = 0; row * stepY < image.Height + textHeight; row++)
            {
                for (int column = 0; column * stepX < image.Width + textWidth; column++)
                {
                    DrawWatermarkText(image, text, font, column * stepX, row * stepY, stroke, fontSize, fill, alpha);
                }
            }

            return;
        }

        (int x, int y) = WatermarkOrigin(position, image, textWidth, textHeight, margin);
        DrawWatermarkText(image, text, font, x, y, stroke, fontSize, fill, alpha);
    }

    private static void DrawWatermarkText(
        MagickImage image,
        string text,
        string font,
        int x,
        int y,
        int strokeWidth,
        double fontPointsize,
        MagickColor fill,
        ushort alpha)
    {
        var drawables = new List<IDrawable>
        {
            new DrawableFont(font),
            new DrawableFontPointSize(fontPointsize),
            new DrawableFillColor(fill),
            new DrawableStrokeColor(new MagickColor(0, 0, 0, alpha)),
            new DrawableStrokeWidth(strokeWidth),
            new DrawableText(x, y, text),
        };
        image.Draw(drawables.ToArray());
    }

    public static void ApplyImageWatermark(
        MagickImage image,
        string watermarkPath,
        string position,
        int opacityPercent,
        JobRequest request)
    {
        int shortSide = (int)Math.Min(image.Width, image.Height);
        int margin = Math.Max(4, (int)Math.Round(shortSide * 0.03));

        double scalePercent = 25.0;
        if (request.Parameters.TryGetValue("sizePercent", out string? sizeText) &&
            double.TryParse(sizeText!, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) &&
            parsed > 0)
        {
            scalePercent = Math.Clamp(parsed, 1, 100);
        }

        using var mark = new MagickImage(watermarkPath);
        // Only activate alpha when the watermark image has none; AlphaAction.Set
        // would flatten an existing semi-transparent PNG to fully opaque.
        if (!mark.HasAlpha)
        {
            mark.Alpha(AlphaAction.Opaque);
        }

        double scale = Math.Min(shortSide * scalePercent / 100.0 / mark.Width, 1.0);
        int targetWidth = Math.Max(1, (int)Math.Round(mark.Width * scale));
        int targetHeight = Math.Max(1, (int)Math.Round(mark.Height * scale));
        mark.Resize((uint)targetWidth, (uint)targetHeight);

        // Scale the mark's own alpha channel by the requested opacity. The
        // previous Separate(Channels.Alpha) + Composite(CopyAlpha) round-trip
        // silently dropped the channel on marks whose alpha had just been
        // activated, stamping a fully transparent (invisible) watermark.
        mark.Evaluate(
            Channels.Alpha,
            EvaluateOperator.Multiply,
            Math.Clamp(opacityPercent / 100.0, 0, 1));

        (int x, int y) = WatermarkOrigin(position, image, targetWidth, targetHeight, margin);
        image.Composite(mark, x, y, CompositeOperator.Over);
    }

    /// <summary>
    /// Converts a 0-100 opacity percentage into the Q16 quantum value expected by
    /// <see cref="MagickColor"/>'s (red, green, blue, alpha) constructor.
    /// </summary>
    private static ushort AlphaQuantum(int opacityPercent) =>
        (ushort)Math.Clamp(Math.Round(opacityPercent / 100.0 * ushort.MaxValue), 0, ushort.MaxValue);

    private static MagickColor ResolveWatermarkColor(JobRequest request, int opacityPercent)
    {
        ushort alpha = AlphaQuantum(opacityPercent);
        if (request.Parameters.TryGetValue("color", out string? text) &&
            !string.IsNullOrWhiteSpace(text))
        {
            try
            {
                MagickColor parsed = new(text.Trim());
                return new MagickColor(parsed.R, parsed.G, parsed.B, alpha);
            }
            catch (MagickMissingDelegateErrorException)
            {
            }
            catch (ArgumentException)
            {
            }
        }

        // Frozen default: 白字 (white fill). Full white in Q16 is ushort.MaxValue,
        // not 255 - the numeric MagickColor overload interprets every channel as
        // a quantum value.
        return new MagickColor(ushort.MaxValue, ushort.MaxValue, ushort.MaxValue, alpha);
    }

    private static (int X, int Y) WatermarkOrigin(
        string position,
        MagickImage image,
        int width,
        int height,
        int margin) => position.ToLowerInvariant() switch
        {
            "topleft" or "top-left" => (margin, margin),
            "topright" or "top-right" => ((int)image.Width - width - margin, margin),
            "bottomleft" or "bottom-left" => (margin, (int)image.Height - height - margin),
            "center" => ((int)(image.Width - width) / 2, (int)(image.Height - height) / 2),
            _ => ((int)image.Width - width - margin, (int)image.Height - height - margin),
        };

    // ---- background ---------------------------------------------------------

    /// <summary>
    /// Brings the image into a state where alpha and colour operations behave:
    /// TrueColorAlpha in sRGB. Setting <see cref="ColorType"/> when the image has
    /// no alpha channel activates an opaque one (unlike
    /// <see cref="AlphaAction.Activate"/>, which zeroes alpha on bilevel/palette
    /// images on some ImageMagick builds); images that already carry alpha keep
    /// their values because the conversion is type-only.
    /// </summary>
    public static void EnsureTrueColorAlpha(MagickImage image)
    {
        if (image.ColorType is not (ColorType.TrueColor or ColorType.TrueColorAlpha))
        {
            // Both branches converge: the conversion is type-only and, when the
            // image had no alpha, activates an opaque channel.
            image.ColorType = ColorType.TrueColorAlpha;
        }

        if (image.ColorSpace != ColorSpace.sRGB)
        {
            image.ColorSpace = ColorSpace.sRGB;
        }
    }

    /// <summary>
    /// 背景移除 (契约级实现): samples the border pixels, then makes every pixel
    /// within the fuzz distance of those samples transparent and applies the frozen
    /// alpha cleanup (&lt;0.02 → 0, &gt;0.98 → 1) with a 2 px feather. The frozen
    /// MODNet 模型 path (E5) is not bundled; this deterministic mode is what ships
    /// until the locked model asset lands. Documented as such in the handler
    /// progress messages and the implementation status.
    /// </summary>
    public static void RemoveBackground(MagickImage image, int fuzzPercent)
    {
        // Normalise BEFORE any pixel work (verified on Win10 with Magick.NET 15):
        // 1) AlphaAction.Activate on a bilevel/palette image silently zeroes the
        //    whole alpha channel instead of adding an opaque one - setting
        //    ColorType=TrueColorAlpha activates it opaquely instead.
        // 2) A Gray colorspace makes every later colour composite (background
        //    replacement, watermark fills) collapse to luminance, so force sRGB.
        EnsureTrueColorAlpha(image);

        image.BackgroundColor = MagickColors.Transparent;
        image.ColorFuzz = new Percentage(Math.Clamp(fuzzPercent, 0, 100));

        // Sample a ring of border pixels so slightly uneven studio backgrounds
        // still match (near-duplicate samples are skipped).
        var samples = new List<MagickColor>();
        int width = (int)image.Width;
        int height = (int)image.Height;
        int step = Math.Max(1, Math.Min(width, height) / 24);
        var pixels = image.GetPixels();

        void Sample(int x, int y)
        {
            int cx = Math.Clamp(x, 0, width - 1);
            int cy = Math.Clamp(y, 0, height - 1);
            IMagickColor<ushort>? sampled = pixels.GetPixel(cx, cy).ToColor();
            if (sampled is null)
            {
                return;
            }

            MagickColor color = new(sampled.R, sampled.G, sampled.B);
            foreach (MagickColor existing in samples)
            {
                if (Math.Abs(existing.R - color.R) < 2048 &&
                    Math.Abs(existing.G - color.G) < 2048 &&
                    Math.Abs(existing.B - color.B) < 2048)
                {
                    return; // near-duplicate sample, skip
                }
            }

            samples.Add(color);
        }

        for (int x = 0; x < width; x += step)
        {
            Sample(x, 0);
            Sample(x, height - 1);
        }

        for (int y = 0; y < height; y += step)
        {
            Sample(0, y);
            Sample(width - 1, y);
        }

        if (samples.Count == 0)
        {
            Sample(0, 0);
        }

        foreach (MagickColor sample in samples)
        {
            image.Opaque(sample, MagickColors.Transparent);
        }

        CleanAlpha(image);
    }

    /// <summary>Replaces the (transparent) background with a solid colour.</summary>
    public static void ReplaceBackground(MagickImage image, MagickColor background)
    {
        if (!image.HasAlpha)
        {
            return;
        }

        // Same normalisation as RemoveBackground: a Gray destination would
        // collapse the replacement colour to luminance.
        EnsureTrueColorAlpha(image);

        using var flat = new MagickImage(background, image.Width, image.Height);
        flat.Composite(image, CompositeOperator.Over);
        image.Composite(flat, CompositeOperator.Copy);
    }

    /// <summary>Frozen alpha cleanup: &lt;0.02 → 0, &gt;0.98 → 1, 2 px feather.</summary>
    public static void CleanAlpha(MagickImage image)
    {
        using var alpha = image.Separate(Channels.Alpha)[0];
        alpha.Threshold(new Percentage(2));
        alpha.Blur(0, 2.0);
        image.Composite(alpha, CompositeOperator.CopyAlpha);
    }
}
