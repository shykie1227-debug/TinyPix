using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TinyPix.Core.Jobs;
using TinyPix.Core.Tools;

namespace TinyPix.Media.Pdf;

/// <summary>
/// pdf.to-image — FEATURE-MATRIX §6：默认 PNG、150 DPI、全部页、白底；可选 300 DPI；
/// 每页 ≤60 MP；签名 PDF 允许但提示导出不携带签名。输出 <c>{原名}_页_001.png</c> 序列。
/// </summary>
public sealed class PdfToImageHandler : PdfToolHandlerBase
{
    public override string ToolId => "pdf.to-image";

    protected override bool IsModifyTool => false; // read-only

    protected override void ValidateParameters(JobRequest request, List<JobError> errors)
    {
        int dpi = TryInt(request.Parameters, "dpi", 150);
        if (dpi != 150 && dpi != 300)
        {
            errors.Add(new JobError("pdf.to-image.dpi-invalid", $"DPI 必须是 150 或 300，当前 {dpi}。", false));
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

        int dpi = TryInt(context.Request.Parameters, "dpi", 150);
        string pagesSpec = context.Request.Parameters.TryGetValue("pages", out string? p) ? (p ?? "") : "";

        var outputs = new List<string>();
        var failures = new List<JobError>();

        for (int inputIndex = 0; inputIndex < inputs.Count; inputIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string input = inputs[inputIndex];

            try
            {
                IReadOnlyList<int> pageNumbers;
                using (PdfSharp.Pdf.PdfDocument probe = PdfSharp.Pdf.IO.PdfReader.Open(input, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))
                {
                    pageNumbers = PdfPageRanges.Parse(pagesSpec, probe.PageCount, out string? rangeError)
                                  ?? throw new InvalidOperationException(rangeError);
                }

                for (int i = 0; i < pageNumbers.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int pageNumber = pageNumbers[i];
                    try
                    {
                        // Check rendered size before writing: render to bitmap first
                        // to enforce the 60 MP frozen cap, then encode.
#pragma warning disable CA1416 // PDFtoImage is supported on Windows/macOS/Linux (all TinyPix targets)
                        using SkiaSharp.SKBitmap bitmap = PDFtoImage.Conversion.ToImage(
                            input,
                            pageNumber - 1,
                            (string?)null,
                            new PDFtoImage.RenderOptions(
                                Dpi: dpi,
                                BackgroundColor: SkiaSharp.SKColors.White));
#pragma warning restore CA1416

                        long megapixels = bitmap.Width * bitmap.Height / 1_000_000;
                        if (megapixels > 60)
                        {
                            throw new InvalidOperationException($"页 {pageNumber} 超过 60 MP（当前 {megapixels} MP）。");
                        }

                        string outputPath = PdfNaming.SequencePath(input, "_页", "png", i + 1, outputDirectory);
                        using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                        {
                            using SkiaSharp.SKData data = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                            data.SaveTo(fs);
                        }

                        outputs.Add(outputPath);
                    }
                    catch (Exception error)
                    {
                        failures.Add(new JobError("pdf.to-image.page-failed", $"页 {pageNumber} 转换失败：{error.Message}", true));
                    }

                    context.Progress.Report(new JobProgress(
                        (i + 1) * 100.0 / pageNumbers.Count,
                        $"转换中 {i + 1}/{pageNumbers.Count}"));
                }
            }
            catch (Exception error)
            {
                failures.Add(new JobError("pdf.to-image.input-failed", $"{Path.GetFileName(input)}：{error.Message}", true));
            }
        }

        if (failures.Count > 0 && outputs.Count == 0)
        {
            throw new InvalidOperationException($"转换全部失败：{failures[0].Message}");
        }

        context.Progress.Report(new JobProgress(100, $"转换完成 · {outputs.Count} 页"));
        return failures.Count > 0
            ? ToolExecutionResult.Partial(outputs, failures[0])
            : ToolExecutionResult.Success(outputs);
    }
}
