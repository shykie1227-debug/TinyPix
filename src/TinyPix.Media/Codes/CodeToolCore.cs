using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ImageMagick;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;
using ZXing.QrCode.Internal;
using ZXing.Rendering;

namespace TinyPix.Media.Codes;

/// <summary>
/// Frozen barcode formats from FEATURE-MATRIX §7 (E9 ZXing.Net): QR / Code 128 /
/// Code 39 / EAN-13 / EAN-8 / UPC-A for generation AND recognition. No other
/// engine may exist anywhere in the product (frozen acceptance rule).
/// </summary>
public enum CodeFormat
{
    Qr,
    Code128,
    Code39,
    Ean13,
    Ean8,
    UpcA,
}

/// <summary>One decoded symbol: format, text and where it was found.</summary>
public sealed record DecodedCode(CodeFormat Format, string Text, double CenterX, double CenterY)
{
    public string DisplayFormat => Format switch
    {
        CodeFormat.Qr => "QR",
        CodeFormat.Code128 => "Code 128",
        CodeFormat.Code39 => "Code 39",
        CodeFormat.Ean13 => "EAN-13",
        CodeFormat.Ean8 => "EAN-8",
        CodeFormat.UpcA => "UPC-A",
        _ => Format.ToString(),
    };
}

/// <summary>
/// Shared ZXing.Net + Magick.NET plumbing for code.generate / code.scan.
///
/// Two format facts verified empirically against ZXing.Net 0.16.11 (2026-10-19):
/// 1. <see cref="PixelData.Pixels"/> from BarcodeWriterPixelData is FOUR bytes per
///    pixel (BGRA), not three - feeding it to a reader as RGB24 silently returns
///    null. All decode paths here use BitmapFormat.BGRA32 consistently.
/// 2. Code 39 silently DROPS the characters $ % + / (write+read round-trip
///    loses them), so generation rejects them up front instead of shipping a
///    barcode that scans back as different text.
/// </summary>
public static class CodeToolCore
{
    /// <summary>Frozen QR default (FEATURE-MATRIX §7): 512×512 px, 纠错 M.</summary>
    public const int DefaultQrSize = 512;

    /// <summary>Frozen barcode height: 256 px.</summary>
    public const int BarcodeHeight = 256;

    /// <summary>Frozen quiet zones: QR 4 modules, 1-D barcodes 10 modules.</summary>
    public const int QrMarginModules = 4;
    public const int BarcodeMarginModules = 10;

    /// <summary>Frozen result cap: 最多返回 50 个码.</summary>
    public const int MaxResults = 50;

    /// <summary>
    /// Long-edge cap applied before scanning. ZXing TryHarder on a 100 MP raster
    /// costs seconds and ~400 MB per rotation; 4096 px is far beyond anything a
    /// scanner can resolve and keeps the worst case near 64 MB per rotation.
    /// </summary>
    public const int MaxScanLongEdge = 4096;

    private const string Code39Forbidden = "$%+/";

    public static CodeFormat? ParseFormat(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return CodeFormat.Qr;
        }

        return text.Trim().ToUpperInvariant() switch
        {
            "QR" or "QR_CODE" or "QRCODE" => CodeFormat.Qr,
            "CODE128" or "CODE_128" or "CODE-128" => CodeFormat.Code128,
            "CODE39" or "CODE_39" or "CODE-39" => CodeFormat.Code39,
            "EAN13" or "EAN_13" or "EAN-13" => CodeFormat.Ean13,
            "EAN8" or "EAN_8" or "EAN-8" => CodeFormat.Ean8,
            "UPCA" or "UPC_A" or "UPC-A" or "UPC" => CodeFormat.UpcA,
            _ => null,
        };
    }

    public static BarcodeFormat ToZxingFormat(CodeFormat format) => format switch
    {
        CodeFormat.Qr => BarcodeFormat.QR_CODE,
        CodeFormat.Code128 => BarcodeFormat.CODE_128,
        CodeFormat.Code39 => BarcodeFormat.CODE_39,
        CodeFormat.Ean13 => BarcodeFormat.EAN_13,
        CodeFormat.Ean8 => BarcodeFormat.EAN_8,
        CodeFormat.UpcA => BarcodeFormat.UPC_A,
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    public static bool IsOneDimensional(CodeFormat format) => format != CodeFormat.Qr;

    public static bool IsValidErrorCorrection(string? text) =>
        text is null ||
        (text.Trim().ToUpperInvariant() is "L" or "M" or "Q" or "H");

    public static ErrorCorrectionLevel ParseErrorCorrection(string? text) =>
        text?.Trim().ToUpperInvariant() switch
        {
            "L" => ErrorCorrectionLevel.L,
            "Q" => ErrorCorrectionLevel.Q,
            "H" => ErrorCorrectionLevel.H,
            _ => ErrorCorrectionLevel.M,
        };

    /// <summary>
    /// Per-format content rules checked BEFORE calling ZXing so the user gets a
    /// precise TinyPix error instead of a writer exception. ZXing's own messages
    /// are kept as the fallback path (capacity / checksum surprises).
    /// </summary>
    public static string? ValidateContent(CodeFormat format, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "内容不能为空。";
        }

        switch (format)
        {
            case CodeFormat.Code39:
                foreach (char c in content)
                {
                    if (Code39Forbidden.Contains(c))
                    {
                        return $"Code 39 不支持字符 “{c}”（该字符会被条码引擎静默丢弃，导致内容失真）。";
                    }
                }

                break;

            case CodeFormat.Ean13:
                return ValidateDigits(content, 12, 13, "EAN-13", format);
            case CodeFormat.Ean8:
                return ValidateDigits(content, 7, 8, "EAN-8", format);
            case CodeFormat.UpcA:
                return ValidateDigits(content, 11, 12, "UPC-A", format);
        }

        return null;
    }

    private static string? ValidateDigits(string content, int withoutChecksum, int withChecksum, string label, CodeFormat format)
    {
        if (content.Length != withoutChecksum && content.Length != withChecksum)
        {
            return $"{label} 需要 {withoutChecksum} 位数字（自动计算校验位）或 {withChecksum} 位数字（含校验位），当前 {content.Length} 位。";
        }

        foreach (char c in content)
        {
            if (!char.IsAsciiDigit(c))
            {
                return $"{label} 只能包含数字，发现字符 “{c}”。";
            }
        }

        // Full-length input carries a check digit: verify it so a typo is caught
        // before the queue instead of failing inside ZXing (frozen error state
        // "EAN/UPC位数或校验位错"). Short input lets ZXing compute the digit.
        if (content.Length == withChecksum && !CheckDigitMatches(content, format))
        {
            char expected = ComputeCheckDigit(content, format);
            return $"{label} 校验位错误：应为 {expected}，当前为 {content[^1]}。";
        }

        return null;
    }

    /// <summary>
    /// GS1 check digit (mod-10, alternating weights 3/1). Positions are counted
    /// from the RIGHT with the check digit as position 1, so the digit directly
    /// left of the check digit always carries weight 3 - identical for EAN-13,
    /// EAN-8 and UPC-A. Verified against ZXing.Net's own output:
    /// EAN-13 "123456789012"→8, EAN-8 "1234567"→0, UPC-A "12345678901"→2.
    /// </summary>
    private static char ComputeCheckDigit(string digitsWithoutCheck, CodeFormat format)
    {
        int sum = 0;
        bool triple = true; // weight 3 on the digit adjacent to the check digit
        for (int i = digitsWithoutCheck.Length - 1; i >= 0; i--)
        {
            int digit = digitsWithoutCheck[i] - '0';
            sum += triple ? digit * 3 : digit;
            triple = !triple;
        }

        int check = (10 - (sum % 10)) % 10;
        return (char)('0' + check);
    }

    private static bool CheckDigitMatches(string fullDigits, CodeFormat format) =>
        fullDigits[^1] == ComputeCheckDigit(fullDigits.Substring(0, fullDigits.Length - 1), format);

    /// <summary>
    /// Renders one code. PNG goes through Magick.NET (E3 owns every PNG in the
    /// product); SVG is produced by ZXing's own SVG renderer.
    /// </summary>
    public static RenderedCode Render(CodeFormat format, string content, CodeRenderOptions options)
    {
        var encoding = new EncodingOptions
        {
            Width = IsOneDimensional(format) ? options.CanvasWidth : options.Size,
            Height = IsOneDimensional(format) ? BarcodeHeight : options.Size,
            Margin = IsOneDimensional(format) ? BarcodeMarginModules : QrMarginModules,
        };

        if (format == CodeFormat.Qr)
        {
            encoding.Hints[EncodeHintType.ERROR_CORRECTION] = ParseErrorCorrection(options.ErrorCorrection);
        }

        // CRITICAL: without an explicit charset ZXing encodes byte-mode content
        // as ISO-8859-1, silently replacing every non-Latin character with "?"
        // (verified: Chinese content round-tripped as "????" before this hint).
        // UTF-8 is the product-wide text encoding, so pin it on every format.
        // 1-D formats that cannot represent the characters still fail honestly
        // (Code 128 throws "Bad character in input").
        encoding.Hints[EncodeHintType.CHARACTER_SET] = "UTF-8";

        if (options.Output == CodeOutputFormat.Svg)
        {
            var writer = new BarcodeWriterSvg { Format = ToZxingFormat(format), Options = encoding };
            SvgRenderer.SvgImage svg = writer.Write(content);
            return new RenderedCode(
                Array.Empty<byte>(),
                svg.Content,
                svg.Width,
                svg.Height,
                CodeOutputFormat.Svg);
        }

        var pixelWriter = new BarcodeWriterPixelData { Format = ToZxingFormat(format), Options = encoding };
        PixelData pixels = pixelWriter.Write(content);

        // E3 owns PNG encoding: feed ZXing's BGRA raster through Magick.NET.
        var image = new MagickImage();
        image.ReadPixels(
            pixels.Pixels,
            new PixelReadSettings(
                (uint)pixels.Width,
                (uint)pixels.Height,
                StorageType.Char,
                PixelMapping.BGRA));
        byte[] png = image.ToByteArray(MagickFormat.Png);
        image.Dispose();

        return new RenderedCode(png, string.Empty, pixels.Width, pixels.Height, CodeOutputFormat.Png);
    }

    /// <summary>
    /// Decodes every code in a BGRA raster. Implements the frozen scan contract:
    /// TryHarder plus four raster rotations (0/90/180/270), results deduplicated
    /// by (format, text) with the unrotated pass winning, capped at
    /// <see cref="MaxResults"/>.
    /// </summary>
    public static IReadOnlyList<DecodedCode> Decode(byte[] bgra, int width, int height, CodeFormat? filter)
    {
        var found = new List<DecodedCode>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                TryHarder = true,
                PossibleFormats = filter is null
                    ? null
                    : new List<BarcodeFormat> { ToZxingFormat(filter.Value) },
            },
        };

        byte[] current = bgra;
        int currentWidth = width;
        int currentHeight = height;

        for (int rotation = 0; rotation < 4; rotation++)
        {
            if (rotation > 0)
            {
                current = Rotate90Clockwise(current, currentWidth, currentHeight);
                (currentWidth, currentHeight) = (currentHeight, currentWidth);
            }

            // No swallow here: a malformed raster must surface as a per-file
            // failure with a real message (the handler wraps each input in its
            // own try/catch). Silently returning an empty list hid genuine
            // engine errors during verification.
            Result[] results = reader.DecodeMultiple(
                current, currentWidth, currentHeight, RGBLuminanceSource.BitmapFormat.BGRA32) ?? Array.Empty<Result>();

            foreach (Result result in results)
            {
                if (FromZxingFormat(result.BarcodeFormat) is not CodeFormat parsed)
                {
                    continue;
                }

                string key = parsed + "\u0001" + result.Text;
                if (!seen.Add(key))
                {
                    continue;
                }

                found.Add(new DecodedCode(
                    parsed,
                    result.Text,
                    rotation == 0 ? width / 2.0 : currentWidth / 2.0,
                    rotation == 0 ? height / 2.0 : currentHeight / 2.0));

                if (found.Count >= MaxResults)
                {
                    return found;
                }
            }

            if (found.Count > 0 && rotation == 0)
            {
                // Codes found upright: still try the other rotations (a photo can
                // mix orientations), which is what the frozen contract asks for.
                continue;
            }
        }

        return found;
    }

    public static CodeFormat? FromZxingFormat(BarcodeFormat format) => format switch
    {
        BarcodeFormat.QR_CODE => CodeFormat.Qr,
        BarcodeFormat.CODE_128 => CodeFormat.Code128,
        BarcodeFormat.CODE_39 => CodeFormat.Code39,
        BarcodeFormat.EAN_13 => CodeFormat.Ean13,
        BarcodeFormat.EAN_8 => CodeFormat.Ean8,
        BarcodeFormat.UPC_A => CodeFormat.UpcA,
        _ => null,
    };

    /// <summary>Rotates a BGRA raster 90° clockwise.</summary>
    public static byte[] Rotate90Clockwise(byte[] source, int width, int height)
    {
        var rotated = new byte[source.Length];
        const int bytesPerPixel = 4;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int sourceIndex = (y * width + x) * bytesPerPixel;
                int targetX = height - 1 - y;
                int targetY = x;
                int targetIndex = (targetY * height + targetX) * bytesPerPixel;
                Buffer.BlockCopy(source, sourceIndex, rotated, targetIndex, bytesPerPixel);
            }
        }

        return rotated;
    }

    /// <summary>
    /// Loads an image for scanning and returns its BGRA raster. Normalises EXIF
    /// orientation first (the preview shows the oriented image, so the code must
    /// be scanned in that orientation) and caps the long edge at
    /// <see cref="MaxScanLongEdge"/>.
    /// </summary>
    public static (byte[] Pixels, int Width, int Height) LoadForScan(string path)
    {
        using var image = new MagickImage(path);
        image.AutoOrient();

        uint width = image.Width;
        uint height = image.Height;
        double longEdge = Math.Max(width, height);
        if (longEdge > MaxScanLongEdge)
        {
            double scale = MaxScanLongEdge / longEdge;
            image.Scale((uint)Math.Max(1, Math.Round(width * scale)), (uint)Math.Max(1, Math.Round(height * scale)));
        }

        byte[]? pixels = image.GetPixels().ToByteArray(PixelMapping.BGRA);
        if (pixels is null)
        {
            throw new InvalidOperationException("无法读取图片像素数据。");
        }

        return (pixels, (int)image.Width, (int)image.Height);
    }

    /// <summary>
    /// Frozen naming for generated codes: <c>二维码_YYYYMMDD_HHMMSS.png</c>,
    /// <c>条码_YYYYMMDD_HHMMSS.png</c> (SVG only for QR). Batch runs append the
    /// 3-digit sequence, then the ordinary <c> (n)</c> collision rule applies.
    /// </summary>
    public static string BuildGeneratedPath(
        string outputDirectory, CodeFormat format, CodeOutputFormat output, string stamp, int sequence)
    {
        string prefix = format == CodeFormat.Qr ? "二维码" : "条码";
        string extension = output == CodeOutputFormat.Svg ? "svg" : "png";
        string sequenceSuffix = sequence > 0 ? $"_{sequence:D3}" : string.Empty;
        string candidate = Path.Combine(outputDirectory, $"{prefix}_{stamp}{sequenceSuffix}.{extension}");
        return ResolveConflict(candidate);
    }

    /// <summary>Frozen conflict rule: append “ (n)” before the extension, from 1.</summary>
    public static string ResolveConflict(string target)
    {
        if (!File.Exists(target))
        {
            return target;
        }

        string directory = Path.GetDirectoryName(target)!;
        string extension = Path.GetExtension(target);
        string stem = Path.GetFileNameWithoutExtension(target);

        for (int n = 1; n < int.MaxValue; n++)
        {
            string candidate = Path.Combine(directory, $"{stem} ({n}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return target;
    }

    /// <summary>
    /// Frozen atomic write: same-directory temp file, then a rename that refuses
    /// to overwrite. A crash mid-write can never leave a half file under the
    /// final name, and nothing is ever written over an existing file.
    /// </summary>
    public static async Task WriteAtomicAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        string temporary = path + ".tinypix-tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, content, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: false);
        }
        catch
        {
            if (File.Exists(temporary))
            {
                try
                {
                    File.Delete(temporary);
                }
                catch
                {
                    // Best-effort cleanup of the unfinished temp file.
                }
            }

            throw;
        }
    }

    public static async Task WriteAtomicTextAsync(string path, string content, CancellationToken cancellationToken)
    {
        string temporary = path + ".tinypix-tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporary, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporary, path, overwrite: false);
        }
        catch
        {
            if (File.Exists(temporary))
            {
                try
                {
                    File.Delete(temporary);
                }
                catch
                {
                    // Best-effort cleanup of the unfinished temp file.
                }
            }

            throw;
        }
    }

    public static string Stamp() => DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
}

public enum CodeOutputFormat
{
    Png,
    Svg,
}

public sealed record CodeRenderOptions(int Size, string? ErrorCorrection, CodeOutputFormat Output)
{
    /// <summary>
    /// Canvas width for 1-D barcodes. ZXing scales the bars to fill it; 512 px
    /// gives a 4 px module for EAN-13 (95 modules + 2×10 quiet) which scans
    /// reliably while keeping the frozen 256 px height.
    /// </summary>
    public int CanvasWidth => Math.Max(Size, 512);
}

public sealed record RenderedCode(byte[] Png, string Svg, int Width, int Height, CodeOutputFormat Format);
