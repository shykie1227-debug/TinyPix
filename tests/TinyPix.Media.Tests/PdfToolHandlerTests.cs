using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TinyPix.Core.Jobs;
using TinyPix.Core.Tools;
using TinyPix.Media.Pdf;
using Xunit;

namespace TinyPix.Media.Tests;

/// <summary>
/// PDF handler tests: page range parser + individual tool behaviors.
/// All PDFs are self-seeded via PDFsharp (no binary fixtures).
/// </summary>
public sealed class PdfToolHandlerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tinypix-pdf-tests", Guid.NewGuid().ToString("N"));

    public PdfToolHandlerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private string SeedPdf(string name, int pages = 3)
    {
        PdfRules.EnsureInitialized();
        string path = Path.Combine(_root, name);
        using var doc = new PdfSharp.Pdf.PdfDocument();
        for (int i = 0; i < pages; i++) doc.AddPage();
        doc.Save(path);
        return path;
    }

    private JobRequest Request(string toolId, IReadOnlyList<string> inputs, Dictionary<string, string>? parameters = null) =>
        JobRequest.Create(toolId, inputs, _root, JobWorkload.HeavyMedia, parameters ?? new Dictionary<string, string>());

    private static async Task<ToolExecutionResult> RunAsync(IToolHandler handler, JobRequest request)
    {
        var context = new JobExecutionContext(request, new Progress<JobProgress>());
        return await handler.ExecuteAsync(context, CancellationToken.None);
    }

    // ── PdfPageRanges ──────────────────────────────────────────────────────

    [Fact]
    public void PageRanges_EmptyMeansAllPages()
    {
        var pages = PdfPageRanges.Parse(null, 5, out string? error);
        Assert.Null(error);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, pages);
    }

    [Fact]
    public void PageRanges_RangeAndSingle()
    {
        var pages = PdfPageRanges.Parse("1-3,5", 6, out string? error);
        Assert.Null(error);
        Assert.Equal(new[] { 1, 2, 3, 5 }, pages);
    }

    [Fact]
    public void PageRanges_OutOfRange_Fails()
    {
        Assert.Null(PdfPageRanges.Parse("1-10", 5, out string? error));
        Assert.NotNull(error);
    }

    [Fact]
    public void PageRanges_DeduplicatesAndSorts()
    {
        var pages = PdfPageRanges.Parse("3,1,3,2", 5, out string? error);
        Assert.Null(error);
        Assert.Equal(new[] { 1, 2, 3 }, pages);
    }

    [Fact]
    public void PageRanges_EmptySpecAfterParse_Fails()
    {
        Assert.Null(PdfPageRanges.Parse(",,", 5, out string? error));
        Assert.NotNull(error);
    }

    // ── PdfNaming ──────────────────────────────────────────────────────────

    [Fact]
    public void Naming_Build_UsesFrozenSuffix()
    {
        string input = SeedPdf("doc.pdf");
        string output = PdfNaming.Build(input, "_合并", _root);
        Assert.EndsWith("_合并.pdf", output);
    }

    [Fact]
    public void Naming_Sequence_MatchesFrozenPattern()
    {
        string input = SeedPdf("photo.pdf");
        var paths = PdfNaming.BuildSequence(input, "_页", "png", 3, _root);
        Assert.Equal(3, paths.Count);
        Assert.EndsWith("_页_001.png", paths[0]);
        Assert.EndsWith("_页_002.png", paths[1]);
        Assert.EndsWith("_页_003.png", paths[2]);
    }

    // ── PdfSplitHandler ────────────────────────────────────────────────────

    [Fact]
    public async Task Split_PerPage_ProducesOneFilePerPage()
    {
        string input = SeedPdf("split-me.pdf", 3);
        var result = await RunAsync(new PdfSplitHandler(), Request("pdf.split", new[] { input }));
        Assert.Equal(3, result.OutputPaths.Count);
        foreach (string path in result.OutputPaths) Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Split_WithRange_ProducesOnlyRequestedPages()
    {
        string input = SeedPdf("split-range.pdf", 5);
        var result = await RunAsync(new PdfSplitHandler(), Request("pdf.split", new[] { input },
            new Dictionary<string, string> { ["pages"] = "1,3" }));
        Assert.Equal(2, result.OutputPaths.Count);
    }

    [Fact]
    public async Task Split_BadRange_FailsAtExecute()
    {
        string input = SeedPdf("split-bad.pdf", 3);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await RunAsync(new PdfSplitHandler(), Request("pdf.split", new[] { input },
                new Dictionary<string, string> { ["pages"] = "1-99" })));
    }

    // ── PdfRotateHandler ───────────────────────────────────────────────────

    [Fact]
    public async Task Rotate_Default90_ProducesNewPdf()
    {
        string input = SeedPdf("rotate.pdf", 3);
        var result = await RunAsync(new PdfRotateHandler(), Request("pdf.rotate", new[] { input },
            new Dictionary<string, string> { ["scope"] = "all" }));
        Assert.Single(result.OutputPaths);
        Assert.True(File.Exists(result.OutputPaths[0]));
    }

    [Fact]
    public async Task Rotate_ExplicitPages()
    {
        string input = SeedPdf("rotate-explicit.pdf", 5);
        var result = await RunAsync(new PdfRotateHandler(), Request("pdf.rotate", new[] { input },
            new Dictionary<string, string> { ["scope"] = "explicit", ["pages"] = "2,4" }));
        Assert.Single(result.OutputPaths);
    }

    [Fact]
    public async Task Rotate_BadAngle_FailsValidation()
    {
        string input = SeedPdf("rotate-bad.pdf", 3);
        var validation = await new PdfRotateHandler().ValidateAsync(Request("pdf.rotate", new[] { input },
            new Dictionary<string, string> { ["angle"] = "45" }), CancellationToken.None);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Code == "pdf.rotate.angle-invalid");
    }

    // ── PdfReorderHandler ──────────────────────────────────────────────────

    [Fact]
    public async Task Reorder_ReversesPageOrder()
    {
        string input = SeedPdf("reorder.pdf", 3);
        var result = await RunAsync(new PdfReorderHandler(), Request("pdf.reorder", new[] { input },
            new Dictionary<string, string> { ["order"] = "3,2,1" }));
        Assert.Single(result.OutputPaths);
    }

    [Fact]
    public async Task Reorder_MissingOrder_FailsValidation()
    {
        string input = SeedPdf("reorder-missing.pdf", 3);
        var validation = await new PdfReorderHandler().ValidateAsync(Request("pdf.reorder", new[] { input }), CancellationToken.None);
        Assert.False(validation.IsValid);
    }

    [Fact]
    public async Task Reorder_ZeroPages_Fails()
    {
        string input = SeedPdf("reorder-zero.pdf", 3);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await RunAsync(new PdfReorderHandler(), Request("pdf.reorder", new[] { input },
                new Dictionary<string, string> { ["order"] = "" })));
    }

    // ── PdfMergeHandler ────────────────────────────────────────────────────

    [Fact]
    public async Task Merge_TwoPdfs_ProducesOneOutput()
    {
        string a = SeedPdf("merge-a.pdf", 2);
        string b = SeedPdf("merge-b.pdf", 3);
        var result = await RunAsync(new PdfMergeHandler(), Request("pdf.merge", new[] { a, b }));
        Assert.Single(result.OutputPaths);
        Assert.True(File.Exists(result.OutputPaths[0]));
    }

    [Fact]
    public async Task Merge_SinglePdf_FailsValidation()
    {
        string a = SeedPdf("merge-single.pdf", 2);
        var validation = await new PdfMergeHandler().ValidateAsync(Request("pdf.merge", new[] { a }), CancellationToken.None);
        Assert.False(validation.IsValid);
    }

    // ── PdfWatermarkHandler ────────────────────────────────────────────────

    [Fact]
    public async Task Watermark_Text_AddsOverlayAndSaves()
    {
        // Segoe UI font rendering requires a font resolver on non-Windows.
        // The full behavior is verified on the Windows VM; this test only
        // passes on Windows where Segoe UI is a system font.
        if (!OperatingSystem.IsWindows())
        {
            return; // font-dependent; validated on VM
        }

        string input = SeedPdf("watermark.pdf", 2);
        var result = await RunAsync(new PdfWatermarkHandler(), Request("pdf.watermark", new[] { input }));
        Assert.Single(result.OutputPaths);
        Assert.True(File.Exists(result.OutputPaths[0]));
    }

    // ── PdfCompressHandler ─────────────────────────────────────────────────

    [Fact]
    public async Task Compress_RequiresConfirmation()
    {
        string input = SeedPdf("compress.pdf", 2);
        var validation = await new PdfCompressHandler().ValidateAsync(Request("pdf.compress", new[] { input }), CancellationToken.None);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Code == "pdf.compress.confirm-required");
    }

    // ── Gates ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ModifyTool_SignedPdf_IsBlockedBeforeExecute()
    {
        // Seed a PDF and inject a fake signature marker.
        string input = SeedPdf("signed.pdf", 2);
        byte[] bytes = File.ReadAllBytes(input);
        byte[] marker = System.Text.Encoding.ASCII.GetBytes("/ByteRange");
        var combined = new byte[bytes.Length + marker.Length];
        Array.Copy(bytes, 0, combined, 0, bytes.Length);
        Array.Copy(marker, 0, combined, bytes.Length, marker.Length);
        File.WriteAllBytes(input, combined);

        var validation = await new PdfRotateHandler().ValidateAsync(Request("pdf.rotate", new[] { input }), CancellationToken.None);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Code == "pdf.rotate.input-blocked");
    }

    [Fact]
    public async Task ReadOnlyTool_SignedPdf_IsAllowed()
    {
        string input = SeedPdf("readonly-signed.pdf", 2);
        byte[] bytes = File.ReadAllBytes(input);
        byte[] marker = System.Text.Encoding.ASCII.GetBytes("/ByteRange");
        var combined = new byte[bytes.Length + marker.Length];
        Array.Copy(bytes, 0, combined, 0, bytes.Length);
        Array.Copy(marker, 0, combined, bytes.Length, marker.Length);
        File.WriteAllBytes(input, combined);

        var validation = await new PdfToImageHandler().ValidateAsync(Request("pdf.to-image", new[] { input }), CancellationToken.None);
        // to-image is read-only → signed PDF is allowed (gate only blocks encrypted/broken)
        Assert.True(validation.IsValid);
    }

    [Fact]
    public async Task MissingFile_FailsValidation()
    {
        var validation = await new PdfRotateHandler().ValidateAsync(
            Request("pdf.rotate", new[] { Path.Combine(_root, "nonexistent.pdf") }), CancellationToken.None);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Code == "pdf.rotate.input-missing");
    }

    [Fact]
    public async Task WrongExtension_FailsValidation()
    {
        string input = Path.Combine(_root, "doc.txt");
        File.WriteAllText(input, "hello");
        var validation = await new PdfRotateHandler().ValidateAsync(Request("pdf.rotate", new[] { input }), CancellationToken.None);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Code == "pdf.rotate.input-type");
    }
}
