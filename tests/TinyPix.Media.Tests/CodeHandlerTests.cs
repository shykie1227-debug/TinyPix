using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ImageMagick;
using TinyPix.Core.Jobs;
using TinyPix.Core.Tools;
using TinyPix.Media.Codes;
using TinyPix.Media.Tools;
using Xunit;

namespace TinyPix.Media.Tests;

/// <summary>
/// End-to-end contract tests for code.generate / code.scan (FEATURE-MATRIX §7).
/// These run the real E3 (Magick.NET) + E9 (ZXing.Net) pipeline - generate a
/// code, write the PNG, scan it back - so they execute on the Windows build
/// agent / VM, exactly like the image workbench suite. Every test writes into
/// its own temp directory and never touches the repository.
/// </summary>
public sealed class CodeHandlerTests : IDisposable
{
    private readonly string _root;

    public CodeHandlerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "tinypix-code-handler-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch
        {
            // Temp cleanup is best-effort.
        }
    }

    [Fact]
    public async Task GenerateQrPng_ScansBackWithSameText()
    {
        var handler = new CodeGenerateHandler();
        string output = Path.Combine(_root, "out");
        var request = JobRequest.Create(
            "code.generate",
            Array.Empty<string>(),
            output,
            JobWorkload.LightFile,
            new Dictionary<string, string> { ["content"] = "https://example.com/验证往返" });

        ToolValidationResult validation = await handler.ValidateAsync(request, CancellationToken.None);
        Assert.True(validation.IsValid, string.Join("; ", validation.Errors.Select(e => e.Message)));

        ToolExecutionResult result = await RunAsync(handler, request);
        Assert.Empty(result.PartialFailure is null ? Array.Empty<JobError>() : new[] { result.PartialFailure });
        Assert.Single(result.OutputPaths);

        string png = result.OutputPaths[0];
        Assert.Equal("二维码", Path.GetFileName(png).Split('_')[0]);
        Assert.EndsWith(".png", png, StringComparison.Ordinal);
        Assert.True(new FileInfo(png).Length > 0);

        // The generated PNG must actually decode (this is the whole point of E9+E3).
        ScanOutcome scan = CodeScanHandler.ScanOne(png, CodeFormat.Qr);
        string text = Assert.Single(scan.Codes).Text;
        Assert.Equal("https://example.com/验证往返", text);
    }

    [Fact]
    public async Task GenerateQrSvg_WritesVectorDocument()
    {
        var handler = new CodeGenerateHandler();
        var request = JobRequest.Create(
            "code.generate",
            Array.Empty<string>(),
            Path.Combine(_root, "out"),
            JobWorkload.LightFile,
            new Dictionary<string, string>
            {
                ["content"] = "SVG-OUT",
                ["output"] = "svg",
                ["ecLevel"] = "Q",
                ["size"] = "768",
            });

        ToolExecutionResult result = await RunAsync(handler, request);
        string svg = Assert.Single(result.OutputPaths);
        Assert.EndsWith(".svg", svg, StringComparison.Ordinal);
        string content = File.ReadAllText(svg);
        Assert.Contains("<svg", content, StringComparison.Ordinal);
        Assert.Contains("viewBox=\"0 0 768 768\"", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateBatchFromTxt_UsesSequenceNamingAndScansBack()
    {
        string txt = Path.Combine(_root, "batch.txt");
        File.WriteAllLines(txt, new[] { "FIRST-CODE", "SECOND-CODE", "  ", "THIRD-CODE" });

        var handler = new CodeGenerateHandler();
        var request = JobRequest.Create(
            "code.generate",
            new[] { txt },
            Path.Combine(_root, "out"),
            JobWorkload.LightFile,
            new Dictionary<string, string> { ["format"] = "CODE_128" });

        ToolExecutionResult result = await RunAsync(handler, request);
        Assert.Equal(3, result.OutputPaths.Count);

        // Blank line skipped; frozen _001/_002/_003 sequence.
        Assert.Contains(result.OutputPaths, p => p.EndsWith("_001.png", StringComparison.Ordinal));
        Assert.Contains(result.OutputPaths, p => p.EndsWith("_002.png", StringComparison.Ordinal));
        Assert.Contains(result.OutputPaths, p => p.EndsWith("_003.png", StringComparison.Ordinal));

        foreach (string png in result.OutputPaths.OrderBy(p => p, StringComparer.Ordinal))
        {
            ScanOutcome scan = CodeScanHandler.ScanOne(png, CodeFormat.Code128);
            Assert.Single(scan.Codes);
        }
    }

    [Fact]
    public async Task GenerateEan13_ComputesChecksumAndScansBack()
    {
        var handler = new CodeGenerateHandler();
        var request = JobRequest.Create(
            "code.generate",
            Array.Empty<string>(),
            Path.Combine(_root, "out"),
            JobWorkload.LightFile,
            new Dictionary<string, string> { ["content"] = "123456789012", ["format"] = "EAN_13" });

        ToolExecutionResult result = await RunAsync(handler, request);
        string png = Assert.Single(result.OutputPaths);

        ScanOutcome scan = CodeScanHandler.ScanOne(png, CodeFormat.Ean13);
        Assert.Equal("1234567890128", Assert.Single(scan.Codes).Text); // ZXing computes the 8
    }

    [Fact]
    public async Task Generate_BadEanChecksum_IsRejectedBeforeQueue()
    {
        var handler = new CodeGenerateHandler();
        var request = JobRequest.Create(
            "code.generate",
            Array.Empty<string>(),
            Path.Combine(_root, "out"),
            JobWorkload.LightFile,
            new Dictionary<string, string> { ["content"] = "1234567890123", ["format"] = "EAN_13" });

        ToolValidationResult validation = await handler.ValidateAsync(request, CancellationToken.None);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Code == "code.generate.content-invalid");
    }

    [Fact]
    public async Task Generate_Code39ForbiddenCharacters_AreRejected()
    {
        var handler = new CodeGenerateHandler();
        var request = JobRequest.Create(
            "code.generate",
            Array.Empty<string>(),
            Path.Combine(_root, "out"),
            JobWorkload.LightFile,
            new Dictionary<string, string> { ["content"] = "PRICE 50%+$5/2", ["format"] = "CODE_39" });

        ToolValidationResult validation = await handler.ValidateAsync(request, CancellationToken.None);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Code == "code.generate.content-invalid");
    }

    [Fact]
    public async Task Generate_Utf16Txt_IsRejected()
    {
        string txt = Path.Combine(_root, "utf16.txt");
        File.WriteAllText(txt, "HELLO", Encoding.Unicode);

        var handler = new CodeGenerateHandler();
        var request = JobRequest.Create(
            "code.generate",
            new[] { txt },
            Path.Combine(_root, "out"),
            JobWorkload.LightFile,
            new Dictionary<string, string>());

        ToolValidationResult validation = await handler.ValidateAsync(request, CancellationToken.None);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Code == "code.generate.txt-unreadable");
    }

    [Fact]
    public async Task Scan_MultipleCodesInOneImage_AllFound()
    {
        string first = await GenerateAsync(CodeFormat.Qr, "FIRST");
        string second = await GenerateAsync(CodeFormat.Qr, "SECOND");
        string collage = Path.Combine(_root, "two-codes.png");

        // Canvas must fit two 512×512 QR rasters with margins (a clipped QR
        // cannot decode - the quiet zone is part of the symbol).
        using (var canvas = new MagickImage(MagickColors.White, 1100, 560))
        {
            using var a = new MagickImage(first);
            using var b = new MagickImage(second);
            canvas.Composite(a, 20, 20);
            canvas.Composite(b, 570, 20);
            canvas.Format = MagickFormat.Png;
            canvas.Write(collage);
        }

        ScanOutcome scan = CodeScanHandler.ScanOne(collage, null);
        Assert.Equal(2, scan.Codes.Count);
        Assert.Contains(scan.Codes, c => c.Text == "FIRST");
        Assert.Contains(scan.Codes, c => c.Text == "SECOND");
    }

    [Fact]
    public async Task Scan_RotatedCode_StillFound()
    {
        string png = await GenerateAsync(CodeFormat.Qr, "ROTATED-90");
        string rotated = Path.Combine(_root, "rotated.png");
        using (var image = new MagickImage(png))
        {
            image.Rotate(90);
            image.Write(rotated);
        }

        ScanOutcome scan = CodeScanHandler.ScanOne(rotated, null);
        Assert.Equal("ROTATED-90", Assert.Single(scan.Codes).Text);
    }

    [Fact]
    public async Task Scan_BlankImage_ReportsNoneDetected()
    {
        string blank = Path.Combine(_root, "blank.png");
        using (var image = new MagickImage(MagickColors.White, 400, 400))
        {
            image.Write(blank);
        }

        var handler = new CodeScanHandler();
        var request = JobRequest.Create(
            "code.scan",
            new[] { blank },
            Path.Combine(_root, "out"),
            JobWorkload.LightFile,
            new Dictionary<string, string>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(handler, request));
    }

    [Fact]
    public async Task Scan_ExportsFrozenTxtReport()
    {
        string png = await GenerateAsync(CodeFormat.Qr, "REPORTED");
        var handler = new CodeScanHandler();
        var request = JobRequest.Create(
            "code.scan",
            new[] { png },
            Path.Combine(_root, "out"),
            JobWorkload.LightFile,
            new Dictionary<string, string> { ["export"] = "1" });

        ToolExecutionResult result = await RunAsync(handler, request);
        string report = Assert.Single(result.OutputPaths);
        Assert.EndsWith("_码识别.txt", report, StringComparison.Ordinal);

        string text = File.ReadAllText(report);
        Assert.Contains("TinyPix 二维码与条码识别", text);
        Assert.Contains("识别到 1 个码", text);
        Assert.Contains("QR\tREPORTED", text);
    }

    [Fact]
    public async Task Scan_RejectsUnsupportedFormatsAndFolders()
    {
        string gif = Path.Combine(_root, "tiny.gif");
        using (var image = new MagickImage(MagickColors.White, 16, 16))
        {
            image.Write(gif);
        }

        var handler = new CodeScanHandler();
        var request = JobRequest.Create(
            "code.scan",
            new[] { gif },
            Path.Combine(_root, "out"),
            JobWorkload.LightFile,
            new Dictionary<string, string>());

        ToolValidationResult validation = await handler.ValidateAsync(request, CancellationToken.None);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Code == "code.scan.input.unsupported");
    }

    private async Task<string> GenerateAsync(CodeFormat format, string content)
    {
        var handler = new CodeGenerateHandler();
        string formatText = format switch
        {
            CodeFormat.Qr => "QR",
            CodeFormat.Code128 => "CODE_128",
            CodeFormat.Code39 => "CODE_39",
            CodeFormat.Ean13 => "EAN_13",
            CodeFormat.Ean8 => "EAN_8",
            CodeFormat.UpcA => "UPC_A",
            _ => "QR",
        };

        var request = JobRequest.Create(
            "code.generate",
            Array.Empty<string>(),
            Path.Combine(_root, "gen"),
            JobWorkload.LightFile,
            new Dictionary<string, string> { ["content"] = content, ["format"] = formatText });

        ToolExecutionResult result = await RunAsync(handler, request);
        return Assert.Single(result.OutputPaths);
    }

    private static async Task<ToolExecutionResult> RunAsync(IToolHandler handler, JobRequest request)
    {
        var progress = new Progress<JobProgress>();
        return await handler.ExecuteAsync(
            new JobExecutionContext(request, progress), CancellationToken.None);
    }
}
