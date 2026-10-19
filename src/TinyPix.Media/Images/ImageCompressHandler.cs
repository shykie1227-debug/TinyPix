using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ImageMagick;
using TinyPix.Core.Jobs;
using TinyPix.Core.Tools;

namespace TinyPix.Media.Images;

/// <summary>
/// 图片压缩 (image.compress). Frozen defaults: “标准”映射 JPG/WebP 质量 82、4:2:0、
/// sRGB；PNG CompressionLevel=9 且无损；像素不变；保留透明；默认保留 EXIF。
/// 输出 <c>{原名}_压缩.{原扩展名}</c>；结果更大时仍保留副本并说明。
/// </summary>
public sealed class ImageCompressHandler : ImageToolHandlerBase
{
    public override string ToolId => "image.compress";

    protected override string OutputSuffix => "_压缩";

    /// <summary>“标准” preset quality for JPG/WebP (frozen matrix).</summary>
    protected override int DefaultQuality => 82;

    protected override IReadOnlyList<JobError> ValidateParameters(JobRequest request)
    {
        var errors = new List<JobError>();

        if (request.Parameters.TryGetValue("quality", out string? qualityText) &&
            (!int.TryParse(qualityText, out int quality) || quality < 1 || quality > 100))
        {
            errors.Add(new JobError(
                "image.compress.quality-out-of-range", "质量参数必须在 1–100 之间。", false));
        }

        return errors;
    }

    protected override Task ApplyAsync(
        MagickImage image,
        JobRequest request,
        CancellationToken cancellationToken)
    {
        // Pixel dimensions never change; encoding happens in SaveAsync with the
        // tool-specific default quality (82 for JPG/WebP, PNG level 9 lossless).
        return Task.CompletedTask;
    }
}
