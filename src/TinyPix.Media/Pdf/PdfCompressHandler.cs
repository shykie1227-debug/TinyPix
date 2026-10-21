using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImageMagick;
using TinyPix.Core.Jobs;
using TinyPix.Core.Tools;

namespace TinyPix.Media.Pdf;

/// <summary>
/// pdf.compress — FEATURE-MATRIX §6：页面栅格压缩（非对象优化）：150 DPI 逐页渲染，
/// JPEG 质量 80 编码，重建仅含页面图的 PDF。执行前必须确认将丢失可搜索文字、
/// 链接、书签、表单和矢量清晰度。输出 <c>{原名}_压缩.pdf</c>。
/// </summary>
public sealed class PdfCompressHandler : PdfToolHandlerBase
{
    public override string ToolId => "pdf.compress";

    protected override void ValidateParameters(JobRequest request, List<JobError> errors)
    {
        if (!TryBool(request.Parameters, "confirmLossy"))
        {
            errors.Add(new JobError(
                "pdf.compress.confirm-required",
                "栅格压缩将丢失可搜索文字、链接、书签、表单和矢量清晰度，请确认后继续。",
                false));
        }
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(
        JobExecutionContext context,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        PdfRules.EnsureInitialized();

        var inputs = context.Request.InputPaths.Where(File.Exists).ToList();
        string outputDirectory = context.Request.OutputDirectory;
        Directory.CreateDirectory(outputDirectory);

        const int rasterDpi = 150;
        const int jpegQuality = 80;

        var outputs = new List<string>();
        var failures = new List<JobError>();

        for (int inputIndex = 0; inputIndex < inputs.Count; inputIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string input = inputs[inputIndex];

            try
            {
                long originalSize = new FileInfo(input).Length;
                int pageCount;
                using (PdfSharp.Pdf.PdfDocument probe = PdfSharp.Pdf.IO.PdfReader.Open(input, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))
                {
                    pageCount = probe.PageCount;
                }

                using var document = new PdfSharp.Pdf.PdfDocument();
                for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Render page to JPEG at 150 DPI.
#pragma warning disable CA1416 // PDFtoImage is supported on Windows/macOS/Linux (all TinyPix targets)
                    using SkiaSharp.SKBitmap bitmap = PDFtoImage.Conversion.ToImage(
                        input,
                        pageIndex,
                        (string?)null,
                        new PDFtoImage.RenderOptions(
                            Dpi: rasterDpi,
                            BackgroundColor: SkiaSharp.SKColors.White));
#pragma warning restore CA1416

                    long megapixels = bitmap.Width * bitmap.Height / 1_000_000;
                    if (megapixels > 60)
                    {
                        throw new InvalidOperationException($"页 {pageIndex + 1} 超过 60 MP（当前 {megapixels} MP）。");
                    }

                    // Encode JPEG q80 via SkiaSharp.
                    using SkiaSharp.SKData data = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, jpegQuality);
                    byte[] jpegBytes = data.ToArray();

                    // Embed as full-page image.
                    PdfSharp.Pdf.PdfPage page = document.AddPage();
                    page.Width = PdfSharp.Drawing.XUnit.FromPoint(bitmap.Width * 72.0 / rasterDpi);
                    page.Height = PdfSharp.Drawing.XUnit.FromPoint(bitmap.Height * 72.0 / rasterDpi);

                    string tempJpeg = Path.Combine(Path.GetTempPath(), $"tinypix-compress-{Guid.NewGuid():N}.jpg");
                    try
                    {
                        File.WriteAllBytes(tempJpeg, jpegBytes);
                        using PdfSharp.Drawing.XImage xImage = PdfSharp.Drawing.XImage.FromFile(tempJpeg);
                        using (PdfSharp.Drawing.XGraphics gfx = PdfSharp.Drawing.XGraphics.FromPdfPage(page))
                        {
                            gfx.DrawImage(xImage, 0, 0, page.Width.Point, page.Height.Point);
                        }
                    }
                    finally
                    {
                        try { File.Delete(tempJpeg); } catch { }
                    }

                    context.Progress.Report(new JobProgress(
                        (pageIndex + 1) * 100.0 / pageCount,
                        $"压缩中 {pageIndex + 1}/{pageCount}"));
                }

                string output = PdfNaming.Build(input, "_压缩", outputDirectory);
                document.Save(output);

                long compressedSize = new FileInfo(output).Length;
                outputs.Add(output);

                context.Progress.Report(new JobProgress(
                    (inputIndex + 1) * 100.0 / inputs.Count,
                    $"原 {FormatBytes(originalSize)} → {FormatBytes(compressedSize)}"));
            }
            catch (Exception error)
            {
                failures.Add(new JobError("pdf.compress.failed", $"压缩失败：{Path.GetFileName(input)}：{error.Message}", true));
            }
        }

        if (failures.Count > 0 && outputs.Count == 0)
        {
            throw new InvalidOperationException($"压缩全部失败：{failures[0].Message}");
        }

        context.Progress.Report(new JobProgress(100, $"压缩完成 · {outputs.Count} 个文件"));
        return failures.Count > 0
            ? ToolExecutionResult.Partial(outputs, failures[0])
            : ToolExecutionResult.Success(outputs);
    }

    private static string FormatBytes(long bytes) =>
        bytes > 1024 * 1024 ? $"{bytes / 1024.0 / 1024.0:F1} MB" : $"{bytes / 1024.0:F0} KB";
}
