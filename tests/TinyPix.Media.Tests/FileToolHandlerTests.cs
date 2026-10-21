using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TinyPix.Core.Jobs;
using TinyPix.Core.Tools;
using TinyPix.Media.Pdf;
using TinyPix.Media.Tools;
using UglyToad.PdfPig;
using Xunit;
using PdfSharpDocument = PdfSharp.Pdf.PdfDocument;

namespace TinyPix.Media.Tests;

/// <summary>
/// Contract tests for the Phase 4 file toolbox handlers (FEATURE-MATRIX §8):
/// file.hash / file.rename / file.metadata. Everything here is pure managed
/// code (.NET crypto + FileStream + PdfPig/PDFsharp), so the whole file runs
/// on macOS exactly like on the Windows build agent. The E2 (ffprobe) and E3
/// (Magick.NET) branches of file.metadata are covered by the VM run and the
/// console smoke harness.
/// </summary>
public sealed class FileToolHandlerTests : IDisposable
{
    private readonly string _root;

    public FileToolHandlerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "tinypix-file-tool-tests", Guid.NewGuid().ToString("N"));
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

    // ---------------------------------------------------------------- file.hash

    [Fact]
    public async Task Hash_DefaultSha256_MatchesKnownDigest()
    {
        string input = WriteBytes("payload.bin", new byte[] { 1, 2, 3, 4, 5 });
        string expected = Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3, 4, 5 }));

        ToolExecutionResult result = await RunAsync(new FileHashHandler(), Request(
            "file.hash", new[] { input }, new Dictionary<string, string>()));

        // View 模式不产出文件；摘要经报告导出验证。
        Assert.Empty(result.OutputPaths);
    }

    [Fact]
    public async Task Hash_ExportWritesFrozenReportWithMatchStatus()
    {
        string input = WriteBytes("payload.bin", new byte[] { 9, 8, 7 });
        string expected = Convert.ToHexString(SHA256.HashData(new byte[] { 9, 8, 7 }));

        ToolExecutionResult result = await RunAsync(new FileHashHandler(), Request(
            "file.hash",
            new[] { input },
            new Dictionary<string, string> { ["export"] = "1", ["expected"] = expected }));

        string report = Assert.Single(result.OutputPaths);
        Assert.StartsWith("文件校验值_", Path.GetFileName(report), StringComparison.Ordinal);
        string text = File.ReadAllText(report);
        Assert.Contains("TinyPix 文件校验值", text, StringComparison.Ordinal);
        Assert.Contains("算法：SHA-256", text, StringComparison.Ordinal);
        Assert.Contains(expected, text, StringComparison.Ordinal);
        Assert.Contains("匹配", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hash_Md5OptIn_LabelsCompatibilityOnly()
    {
        string input = WriteBytes("payload.bin", new byte[] { 5, 5, 5 });
        string expected = Convert.ToHexString(MD5.HashData(new byte[] { 5, 5, 5 }));

        ToolExecutionResult result = await RunAsync(new FileHashHandler(), Request(
            "file.hash",
            new[] { input },
            new Dictionary<string, string> { ["algorithm"] = "MD5", ["export"] = "1", ["expected"] = expected }));

        string text = File.ReadAllText(Assert.Single(result.OutputPaths));
        Assert.Contains("MD5（仅兼容用途，不用于安全校验）", text, StringComparison.Ordinal);
        Assert.Contains("匹配", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hash_Mismatch_ReportsNotMatched()
    {
        string input = WriteBytes("payload.bin", new byte[] { 5, 5, 5 });

        ToolExecutionResult result = await RunAsync(new FileHashHandler(), Request(
            "file.hash",
            new[] { input },
            new Dictionary<string, string>
            {
                ["export"] = "1",
                ["expected"] = new string('0', 64),
            }));

        string text = File.ReadAllText(Assert.Single(result.OutputPaths));
        Assert.Contains("不匹配", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hash_MalformedExpected_IsRejectedBeforeQueue()
    {
        string input = WriteBytes("payload.bin", new byte[] { 1 });

        ToolValidationResult validation = await new FileHashHandler().ValidateAsync(
            Request("file.hash", new[] { input }, new Dictionary<string, string> { ["expected"] = "XYZ" }),
            CancellationToken.None);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Code == "file.hash.expected-malformed");
    }

    [Fact]
    public async Task Hash_FolderAndMissingInputs_AreRejected()
    {
        string folder = Path.Combine(_root, "folder");
        Directory.CreateDirectory(folder);

        ToolValidationResult folderCase = await new FileHashHandler().ValidateAsync(
            Request("file.hash", new[] { folder }, new Dictionary<string, string>()), CancellationToken.None);
        Assert.Contains(folderCase.Errors, e => e.Code == "file.hash.input.folder");

        ToolValidationResult missingCase = await new FileHashHandler().ValidateAsync(
            Request("file.hash", new[] { Path.Combine(_root, "nope.bin") }, new Dictionary<string, string>()),
            CancellationToken.None);
        Assert.Contains(missingCase.Errors, e => e.Code == "file.hash.input.missing");
    }

    // -------------------------------------------------------------- file.rename

    [Fact]
    public async Task Rename_DefaultPrefix_CopiesAndNeverTouchesSource()
    {
        string input = WriteBytes("photo.jpg", new byte[] { 1, 2, 3 });
        DateTime before = new FileInfo(input).LastWriteTimeUtc;

        ToolExecutionResult result = await RunAsync(new FileRenameHandler(), Request(
            "file.rename", new[] { input }, new Dictionary<string, string>()));

        string copy = Assert.Single(result.OutputPaths);
        Assert.Equal("已整理_photo.jpg", Path.GetFileName(copy));
        Assert.True(File.Exists(input)); // 源文件严格只读：不移动、不删除
        Assert.Equal(before, new FileInfo(input).LastWriteTimeUtc);
        Assert.Equal(File.ReadAllBytes(input), File.ReadAllBytes(copy));
    }

    [Fact]
    public async Task Rename_Numbering_OrdersByStableFileName()
    {
        // Add order deliberately differs from ordinal file-name order.
        string b = WriteBytes("b.txt", new byte[] { 2 });
        string a = WriteBytes("a.txt", new byte[] { 1 });
        string c = WriteBytes("c.txt", new byte[] { 3 });

        ToolExecutionResult result = await RunAsync(new FileRenameHandler(), Request(
            "file.rename",
            new[] { b, a, c },
            new Dictionary<string, string> { ["numbering"] = "1" }));

        Assert.Equal(3, result.OutputPaths.Count);
        Assert.Contains(result.OutputPaths, p => Path.GetFileName(p) == "已整理_a_001.txt");
        Assert.Contains(result.OutputPaths, p => Path.GetFileName(p) == "已整理_b_002.txt");
        Assert.Contains(result.OutputPaths, p => Path.GetFileName(p) == "已整理_c_003.txt");
    }

    [Fact]
    public async Task Rename_FindReplace_LocksExtensionByDefault()
    {
        string input = WriteBytes("holiday-2024.jpg", new byte[] { 7 });

        ToolExecutionResult result = await RunAsync(new FileRenameHandler(), Request(
            "file.rename",
            new[] { input },
            new Dictionary<string, string> { ["find"] = "2024", ["replace"] = "2025" }));

        // 扩展名锁定（冻结默认）：只改写文件名主干。
        Assert.Equal("已整理_holiday-2025.jpg", Path.GetFileName(Assert.Single(result.OutputPaths)));
    }

    [Fact]
    public async Task Rename_UnlockedExtension_CanRewriteExtension()
    {
        string input = WriteBytes("notes.txt", new byte[] { 7 });

        ToolExecutionResult result = await RunAsync(new FileRenameHandler(), Request(
            "file.rename",
            new[] { input },
            new Dictionary<string, string>
            {
                ["lockExtension"] = "0",
                ["find"] = "txt",
                ["replace"] = "md",
            }));

        Assert.Equal("已整理_notes.md", Path.GetFileName(Assert.Single(result.OutputPaths)));
    }

    [Fact]
    public async Task Rename_Conflict_AppendsFrozenSuffix()
    {
        string input = WriteBytes("dup.txt", new byte[] { 4 });
        string outputDirectory = Path.Combine(_root, "out");
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllBytes(Path.Combine(outputDirectory, "已整理_dup.txt"), new byte[] { 0 });

        ToolExecutionResult result = await RunAsync(new FileRenameHandler(), Request(
            "file.rename", new[] { input }, new Dictionary<string, string>(), outputDirectory));

        Assert.Equal("已整理_dup (1).txt", Path.GetFileName(Assert.Single(result.OutputPaths)));
    }

    [Fact]
    public async Task Rename_IllegalCharacterInComposedName_IsRejectedBeforeQueue()
    {
        string input = WriteBytes("a.txt", new byte[] { 1 });

        // 查找/替换可把非法路径字符写进组合名 → 入队前拦截（保留名/点空格
        // 结尾守卫同走这条路；前缀非空时保留名通常已被屏蔽）。
        ToolValidationResult validation = await new FileRenameHandler().ValidateAsync(
            Request(
                "file.rename",
                new[] { input },
                new Dictionary<string, string> { ["find"] = "a", ["replace"] = "b/c" }),
            CancellationToken.None);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Code == "file.rename.name-invalid");
    }

    // ------------------------------------------------------------ file.metadata

    [Fact]
    public async Task Metadata_View_ProducesNoOutputAndReadsBaseFields()
    {
        string input = WriteBytes("notes.txt", Encoding.UTF8.GetBytes("hello"));

        IReadOnlyList<(string Group, string Name, string Value)> rows =
            FileMetadataHandler.ReadSummary(input, new FakeEngine("ffprobe"));

        Assert.Contains(rows, r => r is { Group: "基础", Name: "文件名" } && r.Value == "notes.txt");
        Assert.Contains(rows, r => r.Name == "大小" && r.Value.Contains("5 字节"));
        Assert.Contains(rows, r => r is { Group: "深度解析" } && r.Value.Contains("不支持"));

        ToolExecutionResult result = await RunAsync(new FileMetadataHandler(new FakeEngine("ffprobe")), Request(
            "file.metadata", new[] { input }, new Dictionary<string, string>()));

        Assert.Empty(result.OutputPaths); // 查看不输出（冻结规则）
    }

    [Fact]
    public async Task Metadata_Export_WritesFrozenReport()
    {
        string input = WriteBytes("notes.txt", Encoding.UTF8.GetBytes("hello"));

        ToolExecutionResult result = await RunAsync(new FileMetadataHandler(new FakeEngine("ffprobe")), Request(
            "file.metadata", new[] { input }, new Dictionary<string, string> { ["export"] = "1" }));

        string report = Assert.Single(result.OutputPaths);
        Assert.EndsWith("notes_信息.txt", report, StringComparison.Ordinal);
        string text = File.ReadAllText(report);
        Assert.Contains("TinyPix 文件与媒体信息", text, StringComparison.Ordinal);
        Assert.Contains("[基础]", text, StringComparison.Ordinal);
        Assert.Contains("文件名：notes.txt", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Metadata_UnsupportedMode_IsRejected()
    {
        string input = WriteBytes("notes.txt", Encoding.UTF8.GetBytes("x"));

        ToolValidationResult validation = await new FileMetadataHandler(new FakeEngine("ffprobe")).ValidateAsync(
            Request("file.metadata", new[] { input }, new Dictionary<string, string> { ["mode"] = "wipe" }),
            CancellationToken.None);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Code == "file.metadata.mode-unsupported");
    }

    [Fact]
    public async Task Metadata_Pdf_ReadsPageCountAndCleansInfoDictionary()
    {
        PdfRules.EnsureInitialized();
        string pdf = Path.Combine(_root, "report.pdf");
        var document = new PdfSharpDocument();
        document.AddPage();
        document.AddPage();
        document.Info.Title = "机密标题";
        document.Info.Author = "张三";
        document.Info.CreationDate = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        document.Save(pdf);

        // 查看：页数/版本/签名状态。
        IReadOnlyList<(string Group, string Name, string Value)> rows =
            FileMetadataHandler.ReadSummary(pdf, new FakeEngine("ffprobe"));
        Assert.Contains(rows, r => r is { Group: "PDF", Name: "页数" } && r.Value == "2");
        Assert.Contains(rows, r => r.Name == "签名状态" && r.Value == "无签名");

        // 清理：只写新副本，Info 全部清空。
        ToolExecutionResult result = await RunAsync(new FileMetadataHandler(new FakeEngine("ffprobe")), Request(
            "file.metadata", new[] { pdf }, new Dictionary<string, string> { ["mode"] = "clean" }));

        string cleaned = Assert.Single(result.OutputPaths);
        Assert.EndsWith("report_已清理.pdf", cleaned, StringComparison.Ordinal);
        Assert.True(File.Exists(pdf)); // 源文件严格只读

        using UglyToad.PdfPig.PdfDocument verify = UglyToad.PdfPig.PdfDocument.Open(cleaned);
        Assert.Equal(2, verify.NumberOfPages);
        Assert.True(string.IsNullOrEmpty(verify.Information.Title));
        Assert.True(string.IsNullOrEmpty(verify.Information.Author));
    }

    [Fact]
    public async Task Metadata_SignedPdf_CleanIsHardBlockedBeforeQueue()
    {
        PdfRules.EnsureInitialized();
        string pdf = Path.Combine(_root, "signed.pdf");
        var document = new PdfSharpDocument();
        document.AddPage();
        document.Save(pdf);

        // 追加一个含 /ByteRange 的签名字典（增量更新签名的典型形态）。
        File.AppendAllText(pdf, "\nsig << /Type /Sig /ByteRange [0 1 2 3] /Contents (00) >>\n%%EOF\n");

        Assert.True(PdfSignatureScanner.HasSignature(pdf));

        ToolValidationResult validation = await new FileMetadataHandler(new FakeEngine("ffprobe")).ValidateAsync(
            Request("file.metadata", new[] { pdf }, new Dictionary<string, string> { ["mode"] = "clean" }),
            CancellationToken.None);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Code == "file.metadata.signed-pdf");

        // 查看不受影响（只读）。
        IReadOnlyList<(string Group, string Name, string Value)> rows =
            FileMetadataHandler.ReadSummary(pdf, new FakeEngine("ffprobe"));
        Assert.Contains(rows, r => r.Name == "签名状态" && r.Value.Contains("含签名"));
    }

    [Fact]
    public async Task Metadata_Clean_UnsupportedTypeFailsPerItem()
    {
        string input = WriteBytes("notes.txt", Encoding.UTF8.GetBytes("x"));

        // 单项全失败时整任务抛出（与 file.hash / code.scan 同一模式）；
        // 混合批次里该文件记为失败项、其余继续。
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RunAsync(new FileMetadataHandler(new FakeEngine("ffprobe")), Request(
                "file.metadata", new[] { input }, new Dictionary<string, string> { ["mode"] = "clean" })));
    }

    // ------------------------------------------------------------------ helpers

    private string WriteBytes(string name, byte[] content)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static JobRequest Request(
        string toolId, IReadOnlyList<string> inputs, Dictionary<string, string> parameters) =>
        Request(toolId, inputs, parameters, Path.Combine(Path.GetTempPath(), "tinypix-file-tool-out", Guid.NewGuid().ToString("N")));

    private static JobRequest Request(
        string toolId,
        IReadOnlyList<string> inputs,
        Dictionary<string, string> parameters,
        string outputDirectory) =>
        JobRequest.Create(toolId, inputs, outputDirectory, JobWorkload.LightFile, parameters);

    private static async Task<ToolExecutionResult> RunAsync(IToolHandler handler, JobRequest request)
    {
        Directory.CreateDirectory(request.OutputDirectory);
        var progress = new Progress<JobProgress>();
        return await handler.ExecuteAsync(
            new JobExecutionContext(request, progress), CancellationToken.None);
    }

    private sealed class FakeEngine : TinyPix.Media.Ffmpeg.IFfmpegEngineProvider
    {
        public FakeEngine(string path) => FfmpegExecutablePath = path;

        public string FfmpegExecutablePath { get; }

        public string FfprobeExecutablePath => "ffprobe";
    }
}
