using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using UglyToad.PdfPig;
using PdfDocumentOpenMode = PdfSharp.Pdf.IO.PdfDocumentOpenMode;
using PdfReader = PdfSharp.Pdf.IO.PdfReader;

namespace TinyPix.Media.Pdf;

/// <summary>
/// E8 PDF 只读/清理规则（FEATURE-MATRIX §8 file.metadata；Phase 5 的 pdf.*
/// 修改型工具同样以 <see cref="CheckCleanAllowed"/> 作为签名硬阻止门禁）：
/// <list type="bullet">
///   <item><description>读取 — PdfPig：页数、版本、XMP 有无、加密状态；签名状态走字节级探测</description></item>
///   <item><description>清理 — PDFsharp：清空 Info 字典全部字段 + 移除 /Metadata（XMP）引用；只写新副本</description></item>
/// </list>
/// 签名 PDF 清理硬阻止（冻结规则：修改副本会使原签名失效，请改用只读导出）。
/// </summary>
public static class PdfRules
{
    /// <summary>
    /// PDFsharp 6.x 是双许可（AGPL 或商业）。TinyPix 随 exe 分发且源码在公开仓库，
    /// 按 AGPL 条款使用；此调用必须在任何 PDFsharp API 之前执行一次。
    /// </summary>
    static PdfRules()
    {
        PdfSharp.PdfSharpLicense.SetAgplLicense();
    }

    /// <summary>
    /// 强制静态构造运行（AGPL 许可选择）。测试与模块初始化器用它保证
    /// “任何 PDFsharp 调用之前许可已就绪”，不依赖 JIT 的顺序巧合。
    /// </summary>
    public static void EnsureInitialized()
    {
        // 故意为空：调用任一静态成员即触发静态构造。
    }

    /// <summary>Maximum bytes scanned for signature markers (signatures live in the trailer area of incremental updates).</summary>
    internal const int MaximumSignatureScanBytes = 64 * 1024 * 1024;

    /// <summary>
    /// 返回 null 表示允许清理；否则返回硬阻止原因。
    /// </summary>
    public static string? CheckCleanAllowed(string path)
    {
        if (PdfSignatureScanner.HasSignature(path))
        {
            return "该 PDF 含数字签名，清理会使签名失效，已阻止。请改用只读查看/导出。";
        }

        try
        {
            using UglyToad.PdfPig.PdfDocument document = PdfDocument.Open(path);
            if (document.IsEncrypted)
            {
                return "该 PDF 已加密，无法在不知道密码的情况下清理元数据。";
            }
        }
        catch (Exception error)
        {
            return $"无法解析该 PDF：{error.Message}";
        }

        return null;
    }

    public static IReadOnlyList<(string Group, string Name, string Value)> ReadRows(string path)
    {
        var rows = new List<(string, string, string)>();
        using UglyToad.PdfPig.PdfDocument document = PdfDocument.Open(path);

        rows.Add(("PDF", "页数", document.NumberOfPages.ToString()));
        rows.Add(("PDF", "版本", $"1.{document.Version:0}"));

        rows.Add(("PDF", "XMP 元数据", document.TryGetXmpMetadata(out _) ? "有" : "无"));

        var information = document.Information;
        string infoSummary = string.Join("；", new[]
        {
            information.Title,
            information.Author,
            information.Subject,
            information.Keywords,
            information.Creator,
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

        rows.Add(("PDF", "文档信息", string.IsNullOrEmpty(infoSummary) ? "无" : infoSummary));
        rows.Add(("PDF", "签名状态", PdfSignatureScanner.HasSignature(path)
            ? "含签名（只读；清理被阻止）"
            : "无签名"));
        rows.Add(("PDF", "加密", document.IsEncrypted ? "已加密" : "未加密"));

        return rows;
    }

    public static void Clean(string input, string output)
    {
        using PdfSharp.Pdf.PdfDocument document = PdfReader.Open(input, PdfDocumentOpenMode.Modify);

        // Info 字典：字符串字段置 null 即移除键；日期键直接删（置 MinValue 会留下 0001 零日期）。
        document.Info.Title = null!;
        document.Info.Author = null!;
        document.Info.Subject = null!;
        document.Info.Keywords = null!;
        document.Info.Creator = null!;
        document.Info.Elements.Remove("/CreationDate");
        document.Info.Elements.Remove("/ModDate");

        // XMP：摘掉目录里的 /Metadata 引用。PDFsharp Save 时会按已清空的 Info
        // 重新生成一份干净 XMP，因此副本不会残留原标题/作者。
        PdfSharp.Pdf.Advanced.PdfCatalog catalog = document.Internals.Catalog;
        if (catalog.Elements.ContainsKey("/Metadata"))
        {
            catalog.Elements.Remove("/Metadata");
        }

        document.Save(output);
    }
}

/// <summary>
/// Byte-level signature detection. /ByteRange only ever appears inside a
/// signature dictionary (Type /Sig), including incremental-update signatures
/// appended after the original document - which object-model parsers may miss.
/// A full-file scan is bounded by <see cref="MaximumSignatureScanBytes"/>.
/// </summary>
public static class PdfSignatureScanner
{
    private static readonly byte[] ByteRangeMarker = Encoding.ASCII.GetBytes("/ByteRange");
    private static readonly byte[] SigTypeMarker = Encoding.ASCII.GetBytes("/Type/Sig");
    private static readonly byte[] SigTypeSpacedMarker = Encoding.ASCII.GetBytes("/Type /Sig");

    public static bool HasSignature(string path)
    {
        var file = new FileInfo(path);
        long remaining = Math.Min(file.Length, PdfRules.MaximumSignatureScanBytes);
        if (remaining <= 0)
        {
            return false;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var buffer = new byte[1024 * 1024];
        var carry = new byte[0];
        int carryLength = 0;

        while (remaining > 0)
        {
            int toRead = (int)Math.Min(buffer.Length, remaining);
            int read = stream.Read(buffer, 0, toRead);
            if (read <= 0)
            {
                break;
            }

            remaining -= read;

            // Search with the previous chunk's tail so markers spanning the
            // chunk boundary are still found. Markers are at most 12 bytes.
            if (carryLength > 0)
            {
                var window = new byte[carryLength + read];
                Array.Copy(carry, 0, window, 0, carryLength);
                Array.Copy(buffer, 0, window, carryLength, read);
                if (ContainsAny(window))
                {
                    return true;
                }
            }
            else if (ContainsAny(buffer.AsSpan(0, read)))
            {
                return true;
            }

            int keep = Math.Min(read, ByteRangeMarker.Length - 1);
            if (carry.Length < keep)
            {
                Array.Resize(ref carry, Math.Max(keep, 16));
            }

            carryLength = keep;
            Array.Copy(buffer, read - keep, carry, 0, keep);
        }

        return false;
    }

    private static bool ContainsAny(byte[] window) => ContainsAny(window.AsSpan());

private static bool ContainsAny(ReadOnlySpan<byte> window) =>
    window.IndexOf(ByteRangeMarker) >= 0 ||
    window.IndexOf(SigTypeMarker) >= 0 ||
    window.IndexOf(SigTypeSpacedMarker) >= 0;
}

/// <summary>
/// Runs <see cref="PdfRules.EnsureInitialized"/> as soon as the TinyPix.Media
/// module is loaded, so no code path (app startup, tests, future Phase 5 pdf.*
/// handlers) can touch PDFsharp before the AGPL license is selected.
/// </summary>
internal static class PdfLicenseBootstrap
{
    // CA2255 建议 ModuleInitializer 只用于应用/源生成器。这里是有意的产品级
    // 保障：PDFsharp 的许可选择必须早于程序集内任何 PDFsharp 调用（包括未来
    // 新增的、绕过 PdfRules 的直调），模块加载时机比任何静态构造都更早。
#pragma warning disable CA2255 // ModuleInitializer 有意用于本程序集的许可引导
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Initialize() => PdfRules.EnsureInitialized();
#pragma warning restore CA2255
}
