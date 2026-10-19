using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ImageMagick;
using ImageMagick.Drawing;
using TinyPix.Core.Jobs;
using TinyPix.Core.Tools;

namespace TinyPix.Media.Images;

/// <summary>
/// 拼图与联系表 (image.collage). Frozen defaults: 联系表 A4 纵向
/// 2480×3508 px / 300 DPI、3 列、间距 24 px、边距 48 px、白底、文件名 12 pt；
/// 拼图默认 2 列、间距 24 px。输出 <c>{首图原名}_联系表_001.jpg</c> 或
/// <c>{首图原名}_拼图.jpg</c>。一个有序图片组一个任务。
/// </summary>
public sealed class ImageCollageHandler : ImageToolHandlerBase
{
    // A4 portrait at 300 DPI (frozen contact-sheet default).
    private const int ContactPageWidth = 2480;
    private const int ContactPageHeight = 3508;

    public override string ToolId => "image.collage";

    protected override string OutputSuffix => "_联系表";

    protected override IReadOnlyList<JobError> ValidateParameters(JobRequest request)
    {
        var errors = new List<JobError>();

        if (request.InputPaths.Count < 2)
        {
            errors.Add(new JobError("image.collage.too-few", "拼图至少需要 2 张图片。", false));
        }

        if (TryGetInt(request.Parameters, "columns", out int columns) && (columns < 1 || columns > 10))
        {
            errors.Add(new JobError(
                "image.collage.columns-out-of-range", "列数必须在 1–10 之间。", false));
        }

        if (TryGetInt(request.Parameters, "gap", out int gap) && gap < 0)
        {
            errors.Add(new JobError("image.collage.gap-negative", "间距不能为负。", false));
        }

        return errors;
    }

    protected override async Task<IReadOnlyList<string>> ProcessInputAsync(
        string input,
        JobExecutionContext context,
        CancellationToken cancellationToken)
    {
        // One ordered group = one task: the first input carries the naming.
        if (!string.Equals(input, context.Request.InputPaths[0], StringComparison.Ordinal))
        {
            return Array.Empty<string>();
        }

        bool contactSheet = (TryGetString(context.Request.Parameters, "mode") ?? "contact")
            .Equals("contact", StringComparison.OrdinalIgnoreCase);

        var outputs = new List<string>();
        int index = 0;

        if (contactSheet)
        {
            int columns = TryGetInt(context.Request.Parameters, "columns", out int c) ? c : 3;
            int gap = TryGetInt(context.Request.Parameters, "gap", out int g) ? g : 24;
            int margin = TryGetInt(context.Request.Parameters, "margin", out int m) ? m : 48;
            MagickColor background = ImageSaveTuning.ResolveBackground(context.Request);
            bool showName = !TryGetBool(context.Request.Parameters, "showFilename", out bool hide) || hide;

            int page = 1;
            while (index < context.Request.InputPaths.Count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var canvas = new MagickImage(background, (uint)ContactPageWidth, (uint)ContactPageHeight);
                canvas.Density = new Density(300, 300, DensityUnit.PixelsPerInch);

                int cellWidth = (ContactPageWidth - margin * 2 - gap * (columns - 1)) / columns;
                int cellHeight = cellWidth; // square cells for a contact sheet
                int rowsPerPage = Math.Max(1, (ContactPageHeight - margin * 2) / (cellHeight + gap));

                for (int row = 0; row < rowsPerPage && index < context.Request.InputPaths.Count; row++)
                {
                    for (int column = 0; column < columns && index < context.Request.InputPaths.Count; column++)
                    {
                        string source = context.Request.InputPaths[index];
                        index++;

                        int x = margin + column * (cellWidth + gap);
                        int y = margin + row * (cellHeight + gap);
                        await PlaceCellAsync(canvas, source, x, y, cellWidth, cellHeight, showName, gap)
                            .ConfigureAwait(false);
                    }
                }

                string outputPath = ImageOutputNaming.BuildSequenceOutputPath(
                    context.Request.InputPaths[0], OutputSuffix, page, "jpg", context.Request.OutputDirectory);
                await SaveAsync(canvas, outputPath, context.Request, cancellationToken).ConfigureAwait(false);
                outputs.Add(outputPath);
                page++;
            }
        }
        else
        {
            // 拼图 (grid): 2 columns, cells sized to the first image.
            int columns = TryGetInt(context.Request.Parameters, "columns", out int c) ? c : 2;
            int gap = TryGetInt(context.Request.Parameters, "gap", out int g) ? g : 24;
            MagickColor background = ImageSaveTuning.ResolveBackground(context.Request);

            using var first = new MagickImage(context.Request.InputPaths[0]);
            int cellWidth = (int)first.Width;
            int cellHeight = (int)first.Height;
            int rows = (context.Request.InputPaths.Count + columns - 1) / columns;
            int canvasWidth = columns * cellWidth + (columns - 1) * gap;
            int canvasHeight = rows * cellHeight + (rows - 1) * gap;

            using var canvas = new MagickImage(background, (uint)canvasWidth, (uint)canvasHeight);

            for (int i = 0; i < context.Request.InputPaths.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int column = i % columns;
                int row = i / columns;
                int x = column * (cellWidth + gap);
                int y = row * (cellHeight + gap);
                await PlaceCellAsync(canvas, context.Request.InputPaths[i], x, y, cellWidth, cellHeight, false, gap)
                    .ConfigureAwait(false);
            }

            string outputPath = ImageOutputNaming.BuildOutputPath(
                context.Request.InputPaths[0], "_拼图", "jpg", context.Request.OutputDirectory);
            await SaveAsync(canvas, outputPath, context.Request, cancellationToken).ConfigureAwait(false);
            outputs.Add(outputPath);
        }

        return outputs;
    }

    /// <summary>
    /// Fits one source image into a cell (aspect preserved, never enlarged) and
    /// optionally draws the file name below it (12 pt per the frozen matrix).
    /// </summary>
    private static async Task PlaceCellAsync(
        MagickImage canvas,
        string source,
        int x,
        int y,
        int cellWidth,
        int cellHeight,
        bool showName,
        int gap)
    {
        using var cell = new MagickImage(source);
        cell.AutoOrient();

        int labelHeight = showName ? (int)Math.Round(cellHeight * 0.06) : 0;
        int imageHeight = cellHeight - labelHeight;

        double scale = Math.Min(cellWidth / (double)cell.Width, imageHeight / (double)cell.Height);
        if (scale > 1)
        {
            scale = 1; // never enlarge inside the cell
        }

        int drawWidth = Math.Max(1, (int)Math.Round(cell.Width * scale));
        int drawHeight = Math.Max(1, (int)Math.Round(cell.Height * scale));
        cell.FilterType = FilterType.Lanczos;
        cell.Resize((uint)drawWidth, (uint)drawHeight);

        int offsetX = x + (cellWidth - drawWidth) / 2;
        int offsetY = y + (imageHeight - drawHeight) / 2;
        canvas.Composite(cell, offsetX, offsetY, CompositeOperator.Over);

        if (showName)
        {
            // 文件名 12 pt at 300 DPI ≈ 50 px.
            double pointSize = 12 * 300 / 72;
            var drawables = new Drawables();
            drawables.Font("Segoe UI");
            drawables.FontPointSize(pointSize);
            drawables.FillColor(MagickColors.Black);
            drawables.Text(x, y + cellHeight - 4, Path.GetFileName(source));
            canvas.Draw(drawables);
        }
    }

    protected override Task ApplyAsync(
        MagickImage image,
        JobRequest request,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
