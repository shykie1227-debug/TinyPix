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
/// 旋转与翻转 (image.transform). Frozen defaults: 旋转 0°；快捷 90/180/270°；
/// 任意角 -180–180°；水平/垂直翻转关；扩展画布；透明源背景透明，否则白。
/// 输出 <c>{原名}_旋转.{原扩展名}</c> 或 <c>{原名}_翻转.{原扩展名}</c>。
/// </summary>
public sealed class ImageTransformHandler : ImageToolHandlerBase
{
    public override string ToolId => "image.transform";

    protected override string OutputSuffix => "_旋转";

    protected override IReadOnlyList<JobError> ValidateParameters(JobRequest request)
    {
        var errors = new List<JobError>();

        if (TryGetDouble(request.Parameters, "rotate", out double angle))
        {
            if (angle < -180 || angle > 180)
            {
                errors.Add(new JobError(
                    "image.transform.angle-out-of-range", "旋转角度必须在 -180°–180° 之间。", false));
            }
        }

        return errors;
    }

    protected override Task ApplyAsync(
        MagickImage image,
        JobRequest request,
        CancellationToken cancellationToken)
    {
        double angle = 0;
        if (TryGetDouble(request.Parameters, "rotate", out double parsed))
        {
            angle = Math.Clamp(parsed, -180, 180);
        }

        bool flipH = TryGetBool(request.Parameters, "flipH", out bool fh) && fh;
        bool flipV = TryGetBool(request.Parameters, "flipV", out bool fv) && fv;

        if (angle == 0 && !flipH && !flipV)
        {
            return Task.CompletedTask; // nothing to do: still emits the renamed copy
        }

        bool expand = !TryGetBool(request.Parameters, "expand", out bool noExpand) || noExpand;
        bool isQuarterTurn = Math.Abs(angle % 90) < 0.001;

        if (Math.Abs(angle) > 0.001)
        {
            if (!expand || isQuarterTurn)
            {
                // 直角旋转 / 不扩展：直接旋转，不补背景。
                image.Rotate(angle);
            }
            else
            {
                // 任意角 + 扩展画布：透明源保持透明，否则白底。
                image.BackgroundColor = image.HasAlpha
                    ? MagickColors.Transparent
                    : MagickColors.White;
                image.Rotate(angle);
            }
        }

        if (flipV)
        {
            image.Flip();
        }

        if (flipH)
        {
            image.Flop();
        }

        long totalPixels = (long)image.Width * image.Height;
        if (totalPixels > ImageFormats.MaxTotalPixels)
        {
            throw new InvalidOperationException("结果超过 100 MP 上限。");
        }

        return Task.CompletedTask;
    }
}
