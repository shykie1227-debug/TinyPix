using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ImageMagick;
using TinyPix.Core.Jobs;
using TinyPix.Core.Tools;

namespace TinyPix.Media.Images;

/// <summary>
/// 图片格式转换 (image.convert). Frozen defaults: 输出 JPG；质量 90；sRGB；DPI 保持；
/// 透明区域默认白且执行前确认；多页 TIFF 默认全部页分别输出
/// (<c>{原名}_页_001.jpg</c>)。单页输出 <c>{原名}_转换.{ext}</c>。
/// </summary>
public sealed class ImageConvertHandler : ImageToolHandlerBase
{
    public override string ToolId => "image.convert";

    protected override string OutputSuffix => "_转换";

    protected override string? ForcedOutputFormat => null;

    protected override IReadOnlyList<JobError> ValidateParameters(JobRequest request)
    {
        var errors = new List<JobError>();

        if (!request.Parameters.TryGetValue("format", out string? format) ||
            !ImageFormats.IsSupportedOutputFormat(format))
        {
            errors.Add(new JobError(
                "image.convert.format-unsupported",
                "请选择受支持的输出格式（JPG/PNG/WebP/BMP/TIFF/GIF）。",
                false));
        }

        if (request.Parameters.TryGetValue("quality", out string? qualityText) &&
            (!int.TryParse(qualityText, out int quality) || quality < 1 || quality > 100))
        {
            errors.Add(new JobError(
                "image.convert.quality-out-of-range", "质量参数必须在 1–100 之间。", false));
        }

        return errors;
    }

    /// <summary>
    /// Multi-page TIFF: every page becomes its own output file with the sequence
    /// naming rule. Single-page sources keep the normal per-input flow.
    /// </summary>
    protected override async Task<IReadOnlyList<string>> ProcessInputAsync(
        string input,
        JobExecutionContext context,
        CancellationToken cancellationToken)
    {
        string format = context.Request.Parameters["format"].ToLowerInvariant();
        string extension = ImageFormats.ExtensionFor(format);
        bool transparentToOpaque = !ImageFormats.SupportsAlpha(ImageFormats.ResolveOutputFormat(format));

        int pageCount;
        try
        {
            using var probe = new MagickImageCollection(input);
            pageCount = probe.Count;
        }
        catch (MagickMissingDelegateErrorException)
        {
            pageCount = 0;
        }

        if (pageCount <= 1)
        {
            return await base.ProcessInputAsync(input, context, cancellationToken).ConfigureAwait(false);
        }

        var outputs = new List<string>();
        using (var collection = new MagickImageCollection(input))
        {
            for (int page = 0; page < collection.Count; page++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // MagickImageCollection's indexer is typed IMagickImage<ushort>;
                // the runtime object is the MagickImage the collection owns.
                MagickImage image = (MagickImage)collection[page];
                image.AutoOrient();

                // Frozen FEATURE-MATRIX naming for multi-page TIFF output is
                // {原名}_页_001.{ext} - deliberately NOT the single-page
                // {原名}_转换.{ext} suffix.
                string outputPath = ImageOutputNaming.BuildSequenceOutputPath(
                    input, "_页", page + 1, extension, context.Request.OutputDirectory);

                if (transparentToOpaque && image.HasAlpha)
                {
                    MagickColor background = ImageSaveTuning.ResolveBackground(context.Request);
                    image.BackgroundColor = background;
                    image.Alpha(AlphaAction.Background);
                }

                await SaveAsync(image, outputPath, context.Request, cancellationToken).ConfigureAwait(false);
                outputs.Add(outputPath);
            }
        }

        return outputs;
    }

    protected override Task ApplyAsync(
        MagickImage image,
        JobRequest request,
        CancellationToken cancellationToken)
    {
        // The base SaveAsync resolves the target format from the output extension;
        // sRGB conversion keeps the colour contract predictable across formats.
        if (image.ColorSpace != ColorSpace.sRGB)
        {
            image.ColorSpace = ColorSpace.sRGB;
        }

        return Task.CompletedTask;
    }
}
