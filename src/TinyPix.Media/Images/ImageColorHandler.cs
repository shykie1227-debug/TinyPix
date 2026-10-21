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
/// 亮度与色彩 (image.color). Frozen defaults: 亮度 0、对比度 0、饱和度 0、色温 0、
/// 锐化 0；各 -100–100；“自动增强”默认关，开启时固定 AutoLevel +
/// ContrastStretch(0.5%, 0.5%)，不做 AI 增强。输出 <c>{原名}_调色.{原扩展名}</c>。
/// </summary>
public sealed class ImageColorHandler : ImageToolHandlerBase
{
    public override string ToolId => "image.color";

    protected override string OutputSuffix => "_调色";

    protected override IReadOnlyList<JobError> ValidateParameters(JobRequest request)
    {
        var errors = new List<JobError>();

        foreach (string key in new[] { "brightness", "contrast", "saturation", "temperature", "sharpen" })
        {
            if (TryGetDouble(request.Parameters, key, out double value) && (value < -100 || value > 100))
            {
                errors.Add(new JobError(
                    $"image.color.{key}-out-of-range",
                    $"{key} 必须在 -100–100 之间。",
                    false));
            }
        }

        return errors;
    }

    protected override Task ApplyAsync(
        MagickImage image,
        JobRequest request,
        CancellationToken cancellationToken)
    {
        int brightness = RangeParam(request, "brightness");
        int contrast = RangeParam(request, "contrast");
        int saturation = RangeParam(request, "saturation");
        int temperature = RangeParam(request, "temperature");
        int sharpen = RangeParam(request, "sharpen");

        bool autoEnhance = TryGetBool(request.Parameters, "autoEnhance", out bool auto) && auto;

        ImageOperations.ApplyColor(
            image, brightness, contrast, saturation, temperature, sharpen);

        if (autoEnhance)
        {
            ImageOperations.ApplyAutoEnhance(image);
        }

        return Task.CompletedTask;
    }

    private static int RangeParam(JobRequest request, string key)
    {
        if (TryGetDouble(request.Parameters, key, out double value))
        {
            return (int)Math.Round(Math.Clamp(value, -100, 100));
        }

        return 0;
    }
}
