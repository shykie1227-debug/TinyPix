using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ImageMagick;
using TinyPix.Core.Jobs;
using TinyPix.Core.Tools;

namespace TinyPix.Media.Images;

/// <summary>
/// 图片裁切 (image.crop). Frozen defaults: 默认全图自由比例；可选 1:1、4:3、3:2、
/// 16:9；拉直 0°；超出区域禁止；透明源选择 JPG 时必须先选背景色（默认白）。
/// 输出 <c>{原名}_裁切.{原扩展名}</c>。
/// </summary>
public sealed class ImageCropHandler : ImageToolHandlerBase
{
    public override string ToolId => "image.crop";

    protected override string OutputSuffix => "_裁切";

    protected override IReadOnlyList<JobError> ValidateParameters(JobRequest request)
    {
        var errors = new List<JobError>();

        TryGetInt(request.Parameters, "x", out int x);
        TryGetInt(request.Parameters, "y", out int y);
        TryGetInt(request.Parameters, "width", out int width);
        TryGetInt(request.Parameters, "height", out int height);

        if (x < 0 || y < 0)
        {
            errors.Add(new JobError("image.crop.origin-negative", "裁切起点不能为负。", false));
        }

        if (width < 0 || height < 0)
        {
            errors.Add(new JobError("image.crop.size-negative", "裁切尺寸不能为负。", false));
        }

        if (width > 32768 || height > 32768)
        {
            errors.Add(new JobError(
                "image.crop.size-out-of-range", "裁切尺寸必须在 1–32768 px 之间。", false));
        }

        if (TryGetDouble(request.Parameters, "straighten", out double angle) && Math.Abs(angle) > 0.001)
        {
            if (angle < -45 || angle > 45)
            {
                errors.Add(new JobError(
                    "image.crop.straighten-out-of-range", "拉直角度必须在 -45°–45° 之间。", false));
            }
        }

        return errors;
    }

    protected override Task ApplyAsync(
        MagickImage image,
        JobRequest request,
        CancellationToken cancellationToken)
    {
        int imageWidth = (int)image.Width;
        int imageHeight = (int)image.Height;

        int? x = TryGetInt(request.Parameters, "x", out int px) ? px : null;
        int? y = TryGetInt(request.Parameters, "y", out int py) ? py : null;
        int? width = TryGetInt(request.Parameters, "width", out int pw) && pw > 0 ? pw : null;
        int? height = TryGetInt(request.Parameters, "height", out int ph) && ph > 0 ? ph : null;
        double? ratio = ImageOperations.ParseRatio(TryGetString(request.Parameters, "ratio"));

        ImageOperations.CropRect rect = ImageOperations.ResolveCrop(image, x, y, width, height, ratio);

        if (rect.X + rect.Width > imageWidth || rect.Y + rect.Height > imageHeight)
        {
            throw new InvalidOperationException(
                $"裁切区域 {rect.Width}×{rect.Height}@({rect.X},{rect.Y}) 超出图片边界 {imageWidth}×{imageHeight}。");
        }

        if (TryGetDouble(request.Parameters, "straighten", out double angle) && Math.Abs(angle) > 0.001)
        {
            // 拉直：先扩展画布再旋转，避免透明三角。
            image.BackgroundColor = MagickColors.White;
            image.Rotate(angle);
        }

        ImageOperations.Crop(image, rect);
        return Task.CompletedTask;
    }
}
