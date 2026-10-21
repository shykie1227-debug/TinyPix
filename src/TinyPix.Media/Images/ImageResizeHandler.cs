using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ImageMagick;
using TinyPix.Core.Jobs;
using TinyPix.Core.Tools;
using TinyPix.Media.Images;

namespace TinyPix.Media.Images;

/// <summary>
/// 图片缩放 (image.resize). Frozen defaults (FEATURE-MATRIX §4): 尺寸保持、
/// 锁定比例、不放大、“高质量”=LanczosFilter；宽/高 1–32768 px，总像素 ≤ 100 MP；
/// DPI 元数据保持。输出 <c>{原名}_缩放.{原扩展名}</c>。
/// </summary>
public sealed class ImageResizeHandler : ImageToolHandlerBase
{
    public override string ToolId => "image.resize";

    protected override string OutputSuffix => "_缩放";

    protected override IReadOnlyList<JobError> ValidateParameters(JobRequest request)
    {
        var errors = new List<JobError>();

        bool hasWidth = TryGetInt(request.Parameters, "width", out int width);
        bool hasHeight = TryGetInt(request.Parameters, "height", out int height);
        bool hasPercentage = TryGetDouble(request.Parameters, "percentage", out double percentage);

        if (!hasWidth && !hasHeight && !hasPercentage)
        {
            errors.Add(new JobError(
                "image.resize.missing-size", "请指定目标宽度、高度或缩放百分比。", false));
            return errors;
        }

        if ((hasWidth && (width < 1 || width > 32768)) ||
            (hasHeight && (height < 1 || height > 32768)))
        {
            errors.Add(new JobError(
                "image.resize.size-out-of-range", "宽高必须在 1–32768 px 之间。", false));
        }

        if (hasPercentage && (percentage < 1 || percentage > 1000))
        {
            errors.Add(new JobError(
                "image.resize.percentage-out-of-range", "缩放百分比必须在 1–1000 之间。", false));
        }

        return errors;
    }

    protected override Task ApplyAsync(
        MagickImage image,
        JobRequest request,
        CancellationToken cancellationToken)
    {
        bool keepAspect = !TryGetBool(request.Parameters, "keepAspect", out bool lockOff) || lockOff;
        bool noEnlarge = !TryGetBool(request.Parameters, "noEnlarge", out bool enlarge) || enlarge;

        int? width = TryGetInt(request.Parameters, "width", out int w) ? w : null;
        int? height = TryGetInt(request.Parameters, "height", out int h) ? h : null;
        double? percentage = TryGetDouble(request.Parameters, "percentage", out double p) ? p : null;

        (int targetWidth, int targetHeight) = ImageOperations.ResolveTargetSize(
            image, width, height, percentage, keepAspect, noEnlarge);

        long totalPixels = (long)targetWidth * targetHeight;
        if (totalPixels > ImageFormats.MaxTotalPixels)
        {
            throw new InvalidOperationException(
                $"输出像素 {targetWidth}×{targetHeight} 超过 100 MP 上限。");
        }

        if (targetWidth != (int)image.Width || targetHeight != (int)image.Height)
        {
            ImageOperations.Resize(
                image,
                targetWidth,
                targetHeight,
                TryGetString(request.Parameters, "filter"));
        }

        return Task.CompletedTask;
    }
}
