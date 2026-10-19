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
/// 人像背景移除与替换 (image.background). The frozen MODNet model asset (E5) is
/// NOT bundled, so this ships the deterministic contract-level implementation:
/// border-sampling colour-key removal (ImageOperations.RemoveBackground) plus an
/// optional solid-colour replacement. It works on studio/solid backgrounds and
/// on high-contrast subjects; complex natural scenes need the locked model, which
/// is called out in the progress text and the implementation status.
/// 输出 <c>{原名}_背景.{原扩展名}</c>。
/// </summary>
public sealed class ImageBackgroundHandler : ImageToolHandlerBase
{
    public override string ToolId => "image.background";

    protected override string OutputSuffix => "_背景";

    protected override IReadOnlyList<JobError> ValidateParameters(JobRequest request)
    {
        var errors = new List<JobError>();

        string mode = TryGetString(request.Parameters, "mode") ?? "remove";
        if (!mode.Equals("remove", StringComparison.OrdinalIgnoreCase) &&
            !mode.Equals("replace", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new JobError(
                "image.background.mode-unsupported", "模式必须是 remove 或 replace。", false));
        }

        if (TryGetInt(request.Parameters, "fuzz", out int fuzz) && (fuzz < 0 || fuzz > 100))
        {
            errors.Add(new JobError(
                "image.background.fuzz-out-of-range", "容差必须在 0–100 之间。", false));
        }

        string? color = TryGetString(request.Parameters, "color");
        if (color is not null && mode.Equals("replace", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                _ = new MagickColor(color.Trim());
            }
            catch (ArgumentException)
            {
                errors.Add(new JobError(
                    "image.background.color-invalid", $"无法解析背景颜色：{color}", false));
            }
        }

        return errors;
    }

    protected override Task ApplyAsync(
        MagickImage image,
        JobRequest request,
        CancellationToken cancellationToken)
    {
        string mode = TryGetString(request.Parameters, "mode") ?? "remove";
        int fuzz = TryGetInt(request.Parameters, "fuzz", out int f) ? f : 12;

        ImageOperations.RemoveBackground(image, fuzz);

        if (mode.Equals("replace", StringComparison.OrdinalIgnoreCase))
        {
            MagickColor background = ResolveBackground(request);
            ImageOperations.ReplaceBackground(image, background);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Replacement colour. Accepts any ImageMagick colour name or #RRGGBB;
    /// falls back to white (the frozen 透明压平 default).
    /// </summary>
    private static MagickColor ResolveBackground(JobRequest request)
    {
        string? color = TryGetString(request.Parameters, "color");
        if (color is not null)
        {
            try
            {
                return new MagickColor(color.Trim());
            }
            catch (ArgumentException)
            {
            }
        }

        return MagickColors.White;
    }
}
