using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImageMagick;
using TinyPix.Core.Jobs;
using TinyPix.Core.Tools;

namespace TinyPix.Media.IdPhoto;

/// <summary>
/// idphoto.print — FEATURE-MATRIX §5：证件照排版。默认 6 英寸 102×152 mm、300 DPI、
/// 横/竖方向取能容纳更多张的一种；可选 5 英寸 89×127 mm；间距 2 mm、裁切线 0.25 mm、
/// 白底；按模板实际毫米尺寸最大数量排版。输出 <c>{原名}_6英寸排版.jpg</c> 或 5 英寸。
/// </summary>
public sealed class IdPhotoPrintHandler : IToolHandler
{
    public string ToolId => "idphoto.print";

    public async ValueTask<ToolValidationResult> ValidateAsync(JobRequest request, CancellationToken cancellationToken)
    {
        await Task.Yield();
        var errors = new List<JobError>();
        foreach (string input in request.InputPaths)
        {
            if (!File.Exists(input))
            {
                errors.Add(new JobError("idphoto.print.input-missing", $"文件不存在：{input}", false));
            }
        }

        return new ToolValidationResult(errors);
    }

    public ValueTask<ToolEstimate> EstimateAsync(JobRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new ToolEstimate(TimeSpan.FromSeconds(3), null));

    public async Task<ToolExecutionResult> ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken)
    {
        await Task.Yield();

        string paperSize = context.Request.Parameters.TryGetValue("paper", out string? p) ? (p ?? "6inch") : "6inch";
        string presetId = context.Request.Parameters.TryGetValue("preset", out string? pr) ? (pr ?? "office-1in") : "office-1in";
        IdPhotoPreset preset = IdPhotoTemplates.Find(presetId)
            ?? throw new InvalidOperationException($"未知模板：{presetId}");

        // Paper dimensions in mm.
        double paperW = paperSize == "5inch" ? 89 : 102;
        double paperH = paperSize == "5inch" ? 127 : 152;
        const int paperDpi = 300;
        double gapMm = 2;
        double cutLineMm = 0.25;

        // Photo dimensions in mm (from preset).
        double photoW = preset.WidthMm + gapMm;
        double photoH = preset.HeightMm + gapMm;

        // Compute grid for both orientations, pick the one that fits more.
        int colsLandscape = (int)(paperH / photoW);
        int rowsLandscape = (int)(paperW / photoH);
        int colsPortrait = (int)(paperW / photoW);
        int rowsPortrait = (int)(paperH / photoH);
        int countLandscape = colsLandscape * rowsLandscape;
        int countPortrait = colsPortrait * rowsPortrait;

        bool landscape = countLandscape >= countPortrait;
        int cols = landscape ? colsLandscape : colsPortrait;
        int rows = landscape ? rowsLandscape : rowsPortrait;
        double canvasW = landscape ? paperH : paperW;
        double canvasH = landscape ? paperW : paperH;

        // Canvas pixels at 300 DPI.
        int canvasPxW = (int)(canvasW / 25.4 * paperDpi);
        int canvasPxH = (int)(canvasH / 25.4 * paperDpi);
        int photoPxW = (int)(preset.WidthMm / 25.4 * paperDpi);
        int photoPxH = (int)(preset.HeightMm / 25.4 * paperDpi);
        int gapPx = (int)(gapMm / 25.4 * paperDpi);
        int cutPx = Math.Max(1, (int)(cutLineMm / 25.4 * paperDpi));

        string outputDirectory = context.Request.OutputDirectory;
        Directory.CreateDirectory(outputDirectory);

        var outputs = new List<string>();

        // Load photos in input order.
        var photos = new List<MagickImage>();
        foreach (string input in context.Request.InputPaths.Take(cols * rows))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var img = new MagickImage(input);
            img.AutoOrient();
            img.Resize(new MagickGeometry((uint)photoPxW, (uint)photoPxH) { FillArea = true, IgnoreAspectRatio = true });
            photos.Add(img);
        }

        // Compose layout.
        using var canvas = new MagickImage(MagickColors.White, (uint)canvasPxW, (uint)canvasPxH);
        int offsetX = gapPx;
        int offsetY = gapPx;

        for (int row = 0; row < rows && row * cols < photos.Count; row++)
        {
            for (int col = 0; col < cols && row * cols + col < photos.Count; col++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int x = offsetX + col * (photoPxW + gapPx);
                int y = offsetY + row * (photoPxH + gapPx);
                canvas.Composite(photos[row * cols + col], x, y, CompositeOperator.Over);

                // Draw cut lines (0.25 mm grey lines at photo edges).
                DrawCutLines(canvas, x, y, photoPxW, photoPxH, cutPx);
            }
        }

        foreach (var photo in photos) photo.Dispose();

        string label = paperSize == "5inch" ? "5英寸" : "6英寸";
        string output = Path.Combine(outputDirectory, $"{Path.GetFileNameWithoutExtension(context.Request.InputPaths[0])}_{label}排版.jpg");
        canvas.Quality = 95;
        canvas.Write(output, MagickFormat.Jpeg);
        outputs.Add(output);

        context.Progress.Report(new JobProgress(100, $"排版完成 · {label} · {photos.Count} 张 · {(landscape ? "横向" : "纵向")}"));
        return ToolExecutionResult.Success(outputs);
    }

    private static void DrawCutLines(MagickImage canvas, int x, int y, int w, int h, int thickness)
    {
        var lineColor = new MagickColor(200, 200, 200);
        // Top
        canvas.Composite(new MagickImage(lineColor, (uint)w, (uint)thickness), x, y, CompositeOperator.Over);
        // Bottom
        canvas.Composite(new MagickImage(lineColor, (uint)w, (uint)thickness), x, y + h - thickness, CompositeOperator.Over);
        // Left
        canvas.Composite(new MagickImage(lineColor, (uint)thickness, (uint)h), x, y, CompositeOperator.Over);
        // Right
        canvas.Composite(new MagickImage(lineColor, (uint)thickness, (uint)h), x + w - thickness, y, CompositeOperator.Over);
    }
}
