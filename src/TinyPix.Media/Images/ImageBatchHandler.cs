using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ImageMagick;
using TinyPix.Core.Jobs;
using TinyPix.Core.Tools;

namespace TinyPix.Media.Images;

/// <summary>
/// 图片批量处理 (image.batch). Frozen step order (FEATURE-MATRIX §4):
/// 方向归一 → 裁切 → 缩放 → 调色 → 水印 → 格式/压缩. 方向归一 is the
/// AutoOrient the base class already performs, so this handler chains the four
/// pixel steps and leaves the final format/quality decision to the shared save
/// path. Steps whose parameters are absent are skipped, which keeps the batch
/// handler byte-compatible with the individual tools.
/// 输出 <c>{原名}_批量.{原扩展名}</c>（转换时为目标格式扩展名）。
/// </summary>
public sealed class ImageBatchHandler : ImageToolHandlerBase
{
    public override string ToolId => "image.batch";

    protected override string OutputSuffix => "_批量";

    protected override IReadOnlyList<JobError> ValidateParameters(JobRequest request)
    {
        var errors = new List<JobError>();

        bool hasCrop = HasCrop(request);
        bool hasResize = TryGetInt(request.Parameters, "width", out _) ||
                         TryGetInt(request.Parameters, "height", out _) ||
                         TryGetDouble(request.Parameters, "percentage", out _);
        bool hasColor = TryGetDouble(request.Parameters, "brightness", out _) ||
                        TryGetDouble(request.Parameters, "contrast", out _) ||
                        TryGetDouble(request.Parameters, "saturation", out _) ||
                        TryGetDouble(request.Parameters, "temperature", out _) ||
                        TryGetDouble(request.Parameters, "sharpen", out _) ||
                        (TryGetBool(request.Parameters, "autoEnhance", out bool autoEnhance) && autoEnhance);
        bool hasWatermark = TryGetString(request.Parameters, "text") is not null ||
                            TryGetString(request.Parameters, "watermark") is not null;
        bool hasFormat = TryGetString(request.Parameters, "format") is not null;

        if (!hasCrop && !hasResize && !hasColor && !hasWatermark && !hasFormat)
        {
            errors.Add(new JobError(
                "image.batch.no-steps",
                "请至少配置一个处理步骤（裁切、缩放、调色、水印或输出格式）。",
                false));
            return errors;
        }

        // Mirror the individual tools' range checks so the batch path rejects the
        // same values instead of silently clamping them.
        if (TryGetInt(request.Parameters, "width", out int width) && (width < 1 || width > 32768))
        {
            errors.Add(new JobError("image.batch.width-out-of-range", "宽度必须在 1–32768 px 之间。", false));
        }

        if (TryGetInt(request.Parameters, "height", out int height) && (height < 1 || height > 32768))
        {
            errors.Add(new JobError("image.batch.height-out-of-range", "高度必须在 1–32768 px 之间。", false));
        }

        if (TryGetDouble(request.Parameters, "percentage", out double percentage) &&
            (percentage < 1 || percentage > 1000))
        {
            errors.Add(new JobError("image.batch.percentage-out-of-range", "缩放百分比必须在 1–1000 之间。", false));
        }

        foreach (string key in new[] { "brightness", "contrast", "saturation", "temperature", "sharpen" })
        {
            if (TryGetDouble(request.Parameters, key, out double value) && (value < -100 || value > 100))
            {
                errors.Add(new JobError(
                    $"image.batch.{key}-out-of-range", $"{key} 必须在 -100–100 之间。", false));
            }
        }

        string? watermarkPath = TryGetString(request.Parameters, "watermark");
        if (watermarkPath is not null && !File.Exists(watermarkPath))
        {
            errors.Add(new JobError("image.batch.watermark-missing", $"水印图片不存在：{watermarkPath}", false));
        }

        return errors;
    }

    protected override Task ApplyAsync(
        MagickImage image,
        JobRequest request,
        CancellationToken cancellationToken)
    {
        // 2. 裁切 (explicit rect or ratio).
        if (HasCrop(request))
        {
            ImageOperations.CropRect rect = ImageOperations.ResolveCrop(
                image,
                TryGetInt(request.Parameters, "x", out int x) ? x : null,
                TryGetInt(request.Parameters, "y", out int y) ? y : null,
                TryGetInt(request.Parameters, "width", out int w) ? w : null,
                TryGetInt(request.Parameters, "height", out int h) ? h : null,
                ImageOperations.ParseRatio(TryGetString(request.Parameters, "ratio")));
            ImageOperations.Crop(image, rect);
        }

        // 3. 缩放 (same resolution rules as image.resize, including 不放大).
        bool keepAspect = !TryGetBool(request.Parameters, "keepAspect", out bool lockOff) || lockOff;
        bool noEnlarge = !TryGetBool(request.Parameters, "noEnlarge", out bool enlarge) || enlarge;
        int? resizeWidth = TryGetInt(request.Parameters, "width", out int rw) ? rw : null;
        int? resizeHeight = TryGetInt(request.Parameters, "height", out int rh) ? rh : null;
        double? percentage = TryGetDouble(request.Parameters, "percentage", out double p) ? p : null;

        if (resizeWidth is not null || resizeHeight is not null || percentage is not null)
        {
            (int targetWidth, int targetHeight) = ImageOperations.ResolveTargetSize(
                image, resizeWidth, resizeHeight, percentage, keepAspect, noEnlarge);

            long totalPixels = (long)targetWidth * targetHeight;
            if (totalPixels > ImageFormats.MaxTotalPixels)
            {
                throw new InvalidOperationException(
                    $"输出像素 {targetWidth}×{targetHeight} 超过 100 MP 上限。");
            }

            if (targetWidth != (int)image.Width || targetHeight != (int)image.Height)
            {
                ImageOperations.Resize(
                    image, targetWidth, targetHeight, TryGetString(request.Parameters, "filter"));
            }
        }

        // 4. 调色 (same ranges as image.color).
        bool hasColor = TryGetDouble(request.Parameters, "brightness", out _) ||
                        TryGetDouble(request.Parameters, "contrast", out _) ||
                        TryGetDouble(request.Parameters, "saturation", out _) ||
                        TryGetDouble(request.Parameters, "temperature", out _) ||
                        TryGetDouble(request.Parameters, "sharpen", out _);
        bool autoEnhance = TryGetBool(request.Parameters, "autoEnhance", out bool enhance) && enhance;

        if (hasColor || autoEnhance)
        {
            ImageOperations.ApplyColor(
                image,
                RangeParam(request, "brightness"),
                RangeParam(request, "contrast"),
                RangeParam(request, "saturation"),
                RangeParam(request, "temperature"),
                RangeParam(request, "sharpen"));

            if (autoEnhance)
            {
                ImageOperations.ApplyAutoEnhance(image);
            }
        }

        // 5. 水印 (text or image watermark, same defaults as image.watermark).
        string? watermarkPath = TryGetString(request.Parameters, "watermark");
        string? watermarkText = TryGetString(request.Parameters, "text");
        if (watermarkPath is not null || watermarkText is not null)
        {
            int opacity = TryGetInt(request.Parameters, "opacity", out int o) ? o : 70;
            string position = TryGetString(request.Parameters, "position") ?? "bottomRight";
            bool tile = TryGetBool(request.Parameters, "tile", out bool t) && t;

            if (watermarkPath is not null)
            {
                ImageOperations.ApplyImageWatermark(image, watermarkPath, position, opacity, request);
            }
            else
            {
                ImageOperations.ApplyTextWatermark(
                    image, request, watermarkText ?? "TinyPix", position, opacity, tile ? 1 : 0);
            }
        }

        // 6. 格式/压缩 is applied by the shared save path (format + quality).

        return Task.CompletedTask;
    }

    /// <summary>
    /// Crop is configured when an explicit rect is given (x/y/width/height) or a
    /// ratio string parses. Width/height alone mean "resize", not "crop", so the
    /// ratio/explicit-rect rule keeps the two steps unambiguous.
    /// </summary>
    private static bool HasCrop(JobRequest request) =>
        (TryGetInt(request.Parameters, "x", out _) || TryGetInt(request.Parameters, "y", out _)) &&
        (TryGetInt(request.Parameters, "width", out _) || TryGetInt(request.Parameters, "height", out _)) ||
        ImageOperations.ParseRatio(TryGetString(request.Parameters, "ratio")) is not null;

    private static int RangeParam(JobRequest request, string key)
    {
        if (TryGetDouble(request.Parameters, key, out double value))
        {
            return (int)Math.Round(Math.Clamp(value, -100, 100));
        }

        return 0;
    }
}
