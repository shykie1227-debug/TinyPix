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
/// 图片水印 (image.watermark). Frozen defaults: 文字“TinyPix”；Segoe UI；字号=短边
/// 4%，16–192 px；右下；边距=短边 3%；白字 2 px 黑描边；70% 不透明；平铺关。
/// 也接受图片水印（<c>watermark</c> 参数指向 PNG/JPG/WebP）。
/// 输出 <c>{原名}_水印.{原扩展名}</c>。
/// </summary>
public sealed class ImageWatermarkHandler : ImageToolHandlerBase
{
    public override string ToolId => "image.watermark";

    protected override string OutputSuffix => "_水印";

    protected override IReadOnlyList<JobError> ValidateParameters(JobRequest request)
    {
        var errors = new List<JobError>();

        string? watermarkPath = TryGetString(request.Parameters, "watermark");
        string? text = TryGetString(request.Parameters, "text");

        if (watermarkPath is null && text is null)
        {
            // 默认文字水印：frozen default is “TinyPix”.
            text = "TinyPix";
        }

        if (watermarkPath is not null && !File.Exists(watermarkPath))
        {
            errors.Add(new JobError(
                "image.watermark.missing", $"水印图片不存在：{watermarkPath}", false));
        }

        if (TryGetInt(request.Parameters, "opacity", out int opacity) && (opacity < 0 || opacity > 100))
        {
            errors.Add(new JobError(
                "image.watermark.opacity-out-of-range", "不透明度必须在 0–100 之间。", false));
        }

        return errors;
    }

    protected override Task ApplyAsync(
        MagickImage image,
        JobRequest request,
        CancellationToken cancellationToken)
    {
        int opacity = TryGetInt(request.Parameters, "opacity", out int o) ? o : 70;
        string position = TryGetString(request.Parameters, "position") ?? "bottomRight";
        bool tile = TryGetBool(request.Parameters, "tile", out bool t) && t;

        string? watermarkPath = TryGetString(request.Parameters, "watermark");
        if (watermarkPath is not null)
        {
            ImageOperations.ApplyImageWatermark(image, watermarkPath, position, opacity, request);
            return Task.CompletedTask;
        }

        string text = TryGetString(request.Parameters, "text") ?? "TinyPix";
        ImageOperations.ApplyTextWatermark(
            image, request, text, position, opacity, tile ? 1 : 0);
        return Task.CompletedTask;
    }
}
