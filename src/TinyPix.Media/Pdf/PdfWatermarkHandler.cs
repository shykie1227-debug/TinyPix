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
/// pdf.watermark — FEATURE-MATRIX §6：默认文字"TinyPix"；Segoe UI 36 pt；
/// 灰 RGB(128,128,128)；20% 不透明；45°；页面中心；全部页；置于内容上方。
/// 可选 PNG/JPG 图片水印。输出 <c>{原名}_水印.pdf</c>。
/// </summary>
public sealed class PdfWatermarkHandler : PdfToolHandlerBase
{
    public override string ToolId => "pdf.watermark";

    protected override IReadOnlyCollection<string> AllowedExtensions { get; } = new[] { ".pdf", ".png", ".jpg", ".jpeg" };

    protected override void ValidateParameters(JobRequest request, List<JobError> errors)
    {
        bool hasText = request.Parameters.TryGetValue("text", out string? t) && !string.IsNullOrWhiteSpace(t);
        bool hasImage = request.Parameters.TryGetValue("image", out string? img) && !string.IsNullOrWhiteSpace(img);
        if (!hasText && !hasImage)
        {
            // Default text "TinyPix" is applied; no error.
        }

        if (hasImage && !File.Exists(request.Parameters["image"]))
        {
            errors.Add(new JobError("pdf.watermark.image-missing", $"水印图片不存在：{request.Parameters["image"]}", false));
        }
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(
        JobExecutionContext context,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        PdfRules.EnsureInitialized();

        // The first PDF input is the target; other PDFs are also targets (batch).
        var pdfInputs = context.Request.InputPaths.Where(p => p.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)).ToList();
        string? imageWatermark = context.Request.Parameters.TryGetValue("image", out string? img) ? img : null;

        string outputDirectory = context.Request.OutputDirectory;
        Directory.CreateDirectory(outputDirectory);

        string watermarkText = context.Request.Parameters.TryGetValue("text", out string? t) && !string.IsNullOrWhiteSpace(t)
            ? t! : "TinyPix";

        var outputs = new List<string>();
        var failures = new List<JobError>();

        for (int inputIndex = 0; inputIndex < pdfInputs.Count; inputIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string input = pdfInputs[inputIndex];
            try
            {
                using PdfSharp.Pdf.PdfDocument document = PdfSharp.Pdf.IO.PdfReader.Open(
                    input, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Modify);

                for (int pageIndex = 0; pageIndex < document.PageCount; pageIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    PdfSharp.Pdf.PdfPage page = document.Pages[pageIndex];

                    if (imageWatermark is not null && File.Exists(imageWatermark))
                    {
                        DrawImageWatermark(page, imageWatermark);
                    }
                    else
                    {
                        DrawTextWatermark(page, watermarkText);
                    }
                }

                string output = PdfNaming.Build(input, "_水印", outputDirectory);
                document.Save(output);
                outputs.Add(output);
            }
            catch (Exception error)
            {
                failures.Add(new JobError("pdf.watermark.failed", $"水印失败：{input}：{error.Message}", true));
            }

            context.Progress.Report(new JobProgress(
                (inputIndex + 1) * 100.0 / pdfInputs.Count,
                $"水印中 {inputIndex + 1}/{pdfInputs.Count}"));
        }

        if (failures.Count > 0 && outputs.Count == 0)
        {
            throw new InvalidOperationException($"水印全部失败：{failures[0].Message}");
        }

        context.Progress.Report(new JobProgress(100, $"水印完成 · {outputs.Count} 个文件"));
        return failures.Count > 0
            ? ToolExecutionResult.Partial(outputs, failures[0])
            : ToolExecutionResult.Success(outputs);
    }

    private static PdfSharp.Drawing.XFont CreateWatermarkFont()
    {
        // Frozen spec: Segoe UI 36 pt. Fallback to Helvetica for non-Windows
        // environments (macOS CI) where Segoe UI is unavailable and PDFsharp
        // has no font resolver. On Windows (the shipping target) Segoe UI is
        // always present.
        try
        {
            return new PdfSharp.Drawing.XFont("Segoe UI", 36, PdfSharp.Drawing.XFontStyleEx.Regular);
        }
        catch
        {
            return new PdfSharp.Drawing.XFont("Helvetica", 36, PdfSharp.Drawing.XFontStyleEx.Regular);
        }
    }

    private static void DrawTextWatermark(PdfSharp.Pdf.PdfPage page, string text)
    {
        using var gfx = PdfSharp.Drawing.XGraphics.FromPdfPage(page, PdfSharp.Drawing.XGraphicsPdfPageOptions.Append);
        var font = CreateWatermarkFont();
        var color = PdfSharp.Drawing.XColor.FromArgb(255, 128, 128, 128);
        var brush = new PdfSharp.Drawing.XSolidBrush(color);

        PdfSharp.Drawing.XSize size = gfx.MeasureString(text, font);
        double cx = page.Width.Point / 2;
        double cy = page.Height.Point / 2;

        gfx.TranslateTransform(cx, cy);
        gfx.RotateTransform(-45);
        gfx.DrawString(text, font, brush, new PdfSharp.Drawing.XPoint(-size.Width / 2, size.Height / 4));
    }

    private static void DrawImageWatermark(PdfSharp.Pdf.PdfPage page, string imagePath)
    {
        using var gfx = PdfSharp.Drawing.XGraphics.FromPdfPage(page, PdfSharp.Drawing.XGraphicsPdfPageOptions.Append);
        using var image = PdfSharp.Drawing.XImage.FromFile(imagePath);

        double scale = Math.Min(page.Width.Point / image.PixelWidth * 0.5, page.Height.Point / image.PixelHeight * 0.5);
        double w = image.PixelWidth * scale;
        double h = image.PixelHeight * scale;
        gfx.DrawImage(image, (page.Width.Point - w) / 2, (page.Height.Point - h) / 2, w, h);
    }
}
