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
/// pdf.from-image — FEATURE-MATRIX §6：A4 210×297 mm、150 DPI；方向取能最大化
/// 可用面积者；完整显示；边距 10 mm；白底；按列表顺序；透明合成白底。
/// 输出 <c>{首图原名}_图片合成.pdf</c>。
/// </summary>
public sealed class PdfFromImageHandler : PdfToolHandlerBase
{
    public override string ToolId => "pdf.from-image";

    protected override bool IsModifyTool => false;

    protected override IReadOnlyCollection<string> AllowedExtensions { get; } =
        new[] { ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".tiff", ".tif" };

    protected override int MinimumInputs => 1;

    public override async Task<ToolExecutionResult> ExecuteAsync(
        JobExecutionContext context,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        PdfRules.EnsureInitialized();

        var inputs = context.Request.InputPaths.Where(File.Exists).ToList();
        if (inputs.Count == 0)
        {
            throw new InvalidOperationException("至少需要 1 张图片。");
        }

        string outputDirectory = context.Request.OutputDirectory;
        Directory.CreateDirectory(outputDirectory);

        // A4 at 150 DPI: 210mm × 297mm → 1240 × 1754 px
        const int a4WidthMm = 210;
        const int a4HeightMm = 297;
        const int marginMm = 10;

        double a4WidthPt = a4WidthMm / 25.4 * 72;   // 595.3 pt
        double a4HeightPt = a4HeightMm / 25.4 * 72;  // 841.9 pt
        double marginPt = marginMm / 25.4 * 72;

        string output = PdfNaming.Build(inputs[0], "_图片合成", outputDirectory);

        using (var document = new PdfSharp.Pdf.PdfDocument())
        {
            for (int i = 0; i < inputs.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string input = inputs[i];

                // Decode image; composite onto white if it has alpha.
                using var magick = new MagickImage();
                magick.Read(input);
                if (magick.HasAlpha)
                {
                    magick.BackgroundColor = MagickColors.White;
                    magick.Alpha(ImageMagick.AlphaAction.Remove);
                }

                // Orientation: landscape images go on landscape A4 (rotated).
                bool landscape = magick.Width > magick.Height;
                double pageWidth = landscape ? a4HeightPt : a4WidthPt;
                double pageHeight = landscape ? a4WidthPt : a4HeightPt;

                PdfSharp.Pdf.PdfPage page = document.AddPage();
                page.Width = PdfSharp.Drawing.XUnit.FromPoint(pageWidth);
                page.Height = PdfSharp.Drawing.XUnit.FromPoint(pageHeight);

                // Fit image inside margins without distortion.
                double availW = pageWidth - 2 * marginPt;
                double availH = pageHeight - 2 * marginPt;
                double scale = Math.Min(availW / magick.Width, availH / magick.Height);
                double drawW = magick.Width * scale;
                double drawH = magick.Height * scale;
                double x = (pageWidth - drawW) / 2;
                double y = (pageHeight - drawH) / 2;

                // Write the image to a temp PNG, then embed via XImage.
                string tempPng = Path.Combine(Path.GetTempPath(), $"tinypix-fimg-{Guid.NewGuid():N}.png");
                try
                {
                    magick.Write(tempPng, MagickFormat.Png);
                    using PdfSharp.Drawing.XImage xImage = PdfSharp.Drawing.XImage.FromFile(tempPng);
                    using (PdfSharp.Drawing.XGraphics gfx = PdfSharp.Drawing.XGraphics.FromPdfPage(page))
                    {
                        gfx.DrawImage(xImage, x, y, drawW, drawH);
                    }
                }
                finally
                {
                    try { File.Delete(tempPng); } catch { }
                }

                context.Progress.Report(new JobProgress(
                    (i + 1) * 100.0 / inputs.Count,
                    $"合成中 {i + 1}/{inputs.Count}"));
            }

            document.Save(output);
        }

        context.Progress.Report(new JobProgress(100, $"合成完成 · {inputs.Count} 张图片 → PDF"));
        return ToolExecutionResult.Success(new[] { output });
    }
}
