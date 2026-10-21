using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TinyPix.Core.Jobs;
using TinyPix.Core.Tools;
using TinyPix.Media.Codes;
using Xunit;

namespace TinyPix.Media.Tests;

/// <summary>
/// Pure-managed contract tests for the ZXing.Net (E9) code tools: format
/// parsing, per-format content rules (the frozen error vocabulary), raster
/// rotation, frozen naming/collision rules and the atomic write. These run on
/// every platform - no Magick.NET native binary is involved. The Magick.NET
/// backed round-trips live in <see cref="CodeHandlerTests"/> (Windows agent).
/// </summary>
public sealed class CodeToolCoreTests
{
    [Theory]
    [InlineData("QR", CodeFormat.Qr)]
    [InlineData("qr", CodeFormat.Qr)]
    [InlineData("CODE_128", CodeFormat.Code128)]
    [InlineData("code128", CodeFormat.Code128)]
    [InlineData("CODE-39", CodeFormat.Code39)]
    [InlineData("EAN_13", CodeFormat.Ean13)]
    [InlineData("ean-8", CodeFormat.Ean8)]
    [InlineData("UPC_A", CodeFormat.UpcA)]
    [InlineData("UPCA", CodeFormat.UpcA)]
    public void ParseFormat_AcceptsFrozenSpellings(string text, CodeFormat expected)
    {
        Assert.Equal(expected, CodeToolCore.ParseFormat(text));
    }

    [Fact]
    public void ParseFormat_DefaultsToQrAndRejectsUnknown()
    {
        Assert.Equal(CodeFormat.Qr, CodeToolCore.ParseFormat(null));
        Assert.Equal(CodeFormat.Qr, CodeToolCore.ParseFormat("  "));
        Assert.Null(CodeToolCore.ParseFormat("PDF417"));
    }

    [Theory]
    [InlineData(CodeFormat.Ean13, "1234567890123", "校验位错误")] // correct check digit is 8
    [InlineData(CodeFormat.Ean8, "12345671", "校验位错误")]        // correct check digit is 0
    [InlineData(CodeFormat.UpcA, "123456789013", "校验位错误")]    // correct check digit is 2
    [InlineData(CodeFormat.Code39, "50%+$off/2", "%")]        // all four dropped chars; % reported first
    [InlineData(CodeFormat.Code39, "A$B", "$")]
    [InlineData(CodeFormat.Code39, "A+B", "+")]
    [InlineData(CodeFormat.Code39, "A/B", "/")]
    [InlineData(CodeFormat.Ean13, "12345", "位数字")]
    [InlineData(CodeFormat.Ean13, "12345678901a", "数字")]
    [InlineData(CodeFormat.Ean8, "123", "位数字")]
    [InlineData(CodeFormat.UpcA, "1234567890", "位数字")]
    [InlineData(CodeFormat.Qr, "   ", "空")]
    public void ValidateContent_RejectsUnsafeContent(CodeFormat format, string content, string expectedFragment)
    {
        string? error = CodeToolCore.ValidateContent(format, content);
        Assert.NotNull(error);
        Assert.Contains(expectedFragment, error);
    }

    [Theory]
    [InlineData(CodeFormat.Code39, "CODE39-OK 123")]
    [InlineData(CodeFormat.Code128, "Hello 123")]
    [InlineData(CodeFormat.Ean13, "123456789012")]
    [InlineData(CodeFormat.Ean13, "1234567890128")] // 13 digits with the correct check digit
    [InlineData(CodeFormat.Ean8, "1234567")]
    [InlineData(CodeFormat.Ean8, "12345670")] // 8 digits with the correct check digit
    [InlineData(CodeFormat.UpcA, "12345678901")]
    [InlineData(CodeFormat.UpcA, "123456789012")] // 12 digits with the correct check digit
    [InlineData(CodeFormat.Qr, "https://example.com/中文")]
    public void ValidateContent_AcceptsFrozenContent(CodeFormat format, string content)
    {
        Assert.Null(CodeToolCore.ValidateContent(format, content));
    }

    [Fact]
    public void RenderSvg_ProducesDecodableQrDocument()
    {
        RenderedCode rendered = CodeToolCore.Render(
            CodeFormat.Qr,
            "SVG-ROUNDTRIP",
            new CodeRenderOptions(512, "M", CodeOutputFormat.Svg));

        Assert.Equal(CodeOutputFormat.Svg, rendered.Format);
        Assert.Empty(rendered.Png);
        Assert.Contains("<svg", rendered.Svg, StringComparison.Ordinal);
        Assert.Contains("</svg>", rendered.Svg, StringComparison.Ordinal);
        Assert.Equal(512, rendered.Width);
        Assert.Equal(512, rendered.Height);
        // Black modules on a white background (frozen: 黑/白).
        Assert.Contains("rgb(0,0,0)", rendered.Svg, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_ErrorCorrectionLevels_ChangeModuleCount()
    {
        // The frozen L/M/Q/H radio must actually reach ZXing: the same content
        // produces strictly more modules as the correction level rises.
        var counts = new List<int>();
        foreach (string level in new[] { "L", "M", "Q", "H" })
        {
            RenderedCode rendered = CodeToolCore.Render(
                CodeFormat.Qr,
                string.Concat(Enumerable.Repeat("TinyPix EC test 0123456789 ", 30)),
                new CodeRenderOptions(512, level, CodeOutputFormat.Svg));
            counts.Add(rendered.Svg.Split("<rect", StringSplitOptions.None).Length - 1);
        }

        Assert.Equal(4, counts.Count);
        Assert.True(counts[0] < counts[1], $"L({counts[0]}) should be smaller than M({counts[1]})");
        Assert.True(counts[1] < counts[2], $"M({counts[1]}) should be smaller than Q({counts[2]})");
        Assert.True(counts[2] < counts[3], $"Q({counts[2]}) should be smaller than H({counts[3]})");
    }

    [Fact]
    public void Render_QrCapacityExceeded_ThrowsWriterException()
    {
        // Frozen error state: QR 容量超限. ZXing signals it with WriterException.
        var exception = Assert.ThrowsAny<Exception>(() => CodeToolCore.Render(
            CodeFormat.Qr,
            new string('A', 3000),
            new CodeRenderOptions(512, "H", CodeOutputFormat.Png)));
        Assert.Equal("WriterException", exception.GetType().Name);
    }

    [Fact]
    public void Render_BarcodeBadChecksum_ThrowsArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(() => CodeToolCore.Render(
            CodeFormat.Ean13,
            "1234567890123", // 12 digits + wrong check digit
            new CodeRenderOptions(512, "M", CodeOutputFormat.Png)));
        Assert.Contains("checksum", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rotate90Clockwise_MovesCorners()
    {
        // 2×1 raster: [A B] -> rotated 90° cw -> [A / B] as a 1×2 raster.
        byte[] source = { 1, 2, 3, 4, 5, 6, 7, 8 };
        byte[] rotated = CodeToolCore.Rotate90Clockwise(source, 2, 1);

        Assert.Equal(8, rotated.Length);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, rotated.AsSpan(0, 4).ToArray());   // A stays first
        Assert.Equal(new byte[] { 5, 6, 7, 8 }, rotated.AsSpan(4, 4).ToArray());   // B moves below A
    }

    [Fact]
    public void Rotate90Clockwise_IsInvertible()
    {
        var random = new Random(42);
        byte[] source = new byte[4 * 3 * 4];
        random.NextBytes(source);

        byte[] once = CodeToolCore.Rotate90Clockwise(source, 4, 3);
        byte[] twice = CodeToolCore.Rotate90Clockwise(once, 3, 4);
        byte[] thrice = CodeToolCore.Rotate90Clockwise(twice, 4, 3);
        byte[] fourTimes = CodeToolCore.Rotate90Clockwise(thrice, 3, 4);

        Assert.Equal(source, fourTimes);
    }

    [Fact]
    public void BuildGeneratedPath_UsesFrozenNamingAndSequence()
    {
        string root = NewTempDirectory();
        try
        {
            string single = CodeToolCore.BuildGeneratedPath(root, CodeFormat.Qr, CodeOutputFormat.Png, "20261019_203000", 0);
            Assert.Equal(Path.Combine(root, "二维码_20261019_203000.png"), single);

            string batch = CodeToolCore.BuildGeneratedPath(root, CodeFormat.Code128, CodeOutputFormat.Png, "20261019_203000", 7);
            Assert.Equal(Path.Combine(root, "条码_20261019_203000_007.png"), batch);

            string svg = CodeToolCore.BuildGeneratedPath(root, CodeFormat.Qr, CodeOutputFormat.Svg, "20261019_203000", 0);
            Assert.Equal(Path.Combine(root, "二维码_20261019_203000.svg"), svg);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void BuildGeneratedPath_AppliesCollisionRule()
    {
        string root = NewTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "二维码_20261019_203000.png"), "existing");
            File.WriteAllText(Path.Combine(root, "二维码_20261019_203000 (1).png"), "existing");

            string path = CodeToolCore.BuildGeneratedPath(root, CodeFormat.Qr, CodeOutputFormat.Png, "20261019_203000", 0);
            Assert.Equal(Path.Combine(root, "二维码_20261019_203000 (2).png"), path);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task WriteAtomicAsync_WritesFileAndNeverOverwrites()
    {
        string root = NewTempDirectory();
        try
        {
            string target = Path.Combine(root, "atomic.png");
            await CodeToolCore.WriteAtomicAsync(target, new byte[] { 1, 2, 3 }, CancellationToken.None);
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(target));

            // No temp leftovers.
            Assert.Empty(Directory.GetFiles(root, "*.tinypix-tmp"));

            // Refuses to overwrite (frozen conflict rule).
            await Assert.ThrowsAsync<IOException>(() =>
                CodeToolCore.WriteAtomicAsync(target, new byte[] { 9 }, CancellationToken.None));
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(target));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task WriteAtomicTextAsync_WritesUtf8WithoutBom()
    {
        string root = NewTempDirectory();
        try
        {
            string target = Path.Combine(root, "report.txt");
            await CodeToolCore.WriteAtomicTextAsync(target, "二维码 中文", CancellationToken.None);

            byte[] bytes = File.ReadAllBytes(target);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "must not emit a BOM");
            Assert.Equal("二维码 中文", File.ReadAllText(target, Encoding.UTF8));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Stamp_UsesFrozenPattern()
    {
        string stamp = CodeToolCore.Stamp();
        Assert.Equal(15, stamp.Length);
        Assert.True(DateTime.TryParseExact(
            stamp, "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _));
    }

    private static string NewTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "tinypix-code-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
