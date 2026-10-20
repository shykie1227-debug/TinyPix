using System;

namespace TinyPix.Media.Capture;

/// <summary>
/// CPU BGRA32 → NV12 converter with box-filter downscale over a source crop.
/// The frozen quality profile caps output at 1920×1080 and forbids up-scaling
/// ("超大区域等比缩至 1080p 且不放大"), so every frame is scaled down to fit the
/// cap with a simple area average — clean for screen/UI content and deterministic.
/// Colours use the BT.601 limited-range integer coefficients that Media
/// Foundation's NV12 expects.
/// </summary>
public static class Nv12FrameConverter
{
    /// <summary>
    /// Convert the (<paramref name="cropX"/>,<paramref name="cropY"/>,
    /// <paramref name="cropWidth"/>×<paramref name="cropHeight"/>) region of a
    /// <paramref name="sourceWidth"/>×<paramref name="sourceHeight"/> BGRA frame
    /// into a <paramref name="targetWidth"/>×<paramref name="targetHeight"/> NV12 buffer.
    /// </summary>
    public static void ConvertBgraToNv12(
        ReadOnlySpan<byte> bgra,
        int sourceWidth,
        int sourceHeight,
        int cropX,
        int cropY,
        int cropWidth,
        int cropHeight,
        Span<byte> nv12,
        int targetWidth,
        int targetHeight)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || targetWidth <= 0 || targetHeight <= 0)
        {
            throw new ArgumentException("Frame dimensions must be positive.");
        }

        if (cropX < 0 || cropY < 0 || cropWidth <= 0 || cropHeight <= 0 ||
            cropX + cropWidth > sourceWidth || cropY + cropHeight > sourceHeight)
        {
            throw new ArgumentException("Crop rectangle is outside the source frame.");
        }

        if ((targetWidth & 1) != 0 || (targetHeight & 1) != 0)
        {
            throw new ArgumentException("NV12 target dimensions must be even.");
        }

        long required = (long)targetWidth * targetHeight * 3 / 2;
        if (nv12.Length < required)
        {
            throw new ArgumentException($"NV12 buffer too small: {nv12.Length} < {required}.");
        }

        if (bgra.Length < (long)sourceWidth * sourceHeight * 4)
        {
            throw new ArgumentException("BGRA source buffer too small.");
        }

        int ySize = targetWidth * targetHeight;
        Span<byte> yPlane = nv12[..ySize];
        Span<byte> uvPlane = nv12.Slice(ySize, ySize / 2);

        for (int outY = 0; outY < targetHeight; outY++)
        {
            int srcY0 = cropY + outY * cropHeight / targetHeight;
            int srcY1 = Math.Max(srcY0 + 1, cropY + (outY + 1) * cropHeight / targetHeight);
            for (int outX = 0; outX < targetWidth; outX++)
            {
                int srcX0 = cropX + outX * cropWidth / targetWidth;
                int srcX1 = Math.Max(srcX0 + 1, cropX + (outX + 1) * cropWidth / targetWidth);
                SampleBgra(bgra, sourceWidth, srcX0, srcY0, srcX1, srcY1, out int r, out int g, out int b);
                yPlane[outY * targetWidth + outX] = Clamp8(((66 * r + 129 * g + 25 * b + 128) >> 8) + 16);
            }
        }

        for (int outY = 0; outY < targetHeight; outY += 2)
        {
            for (int outX = 0; outX < targetWidth; outX += 2)
            {
                // Chroma is averaged over the 2×2 output luma block area in source space.
                int srcX0 = cropX + outX * cropWidth / targetWidth;
                int srcX1 = Math.Max(srcX0 + 1, cropX + (outX + 2) * cropWidth / targetWidth);
                int srcY0 = cropY + outY * cropHeight / targetHeight;
                int srcY1 = Math.Max(srcY0 + 1, cropY + (outY + 2) * cropHeight / targetHeight);
                SampleBgra(bgra, sourceWidth, srcX0, srcY0, srcX1, srcY1, out int r, out int g, out int b);
                int uvIndex = (outY / 2) * targetWidth + outX;
                uvPlane[uvIndex] = Clamp8(((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128);
                uvPlane[uvIndex + 1] = Clamp8(((112 * r - 94 * g - 18 * b + 128) >> 8) + 128);
            }
        }
    }

    private static void SampleBgra(
        ReadOnlySpan<byte> bgra,
        int sourceWidth,
        int x0,
        int y0,
        int x1,
        int y1,
        out int r,
        out int g,
        out int b)
    {
        int total = 0;
        int sumR = 0, sumG = 0, sumB = 0;
        for (int y = y0; y < y1; y++)
        {
            int row = y * sourceWidth * 4;
            for (int x = x0; x < x1; x++)
            {
                int index = row + x * 4;
                sumB += bgra[index];
                sumG += bgra[index + 1];
                sumR += bgra[index + 2];
                total++;
            }
        }

        r = sumR / total;
        g = sumG / total;
        b = sumB / total;
    }

    private static byte Clamp8(int value) => (byte)Math.Clamp(value, 0, 255);
}
