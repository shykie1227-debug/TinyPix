using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImageMagick;
using TinyPix.Core.Jobs;
using TinyPix.Core.Tools;

namespace TinyPix.Media.Images;

/// <summary>
/// EXIF 查看与清理 (image.exif). Frozen defaults: 默认只看；清理默认移除 GPS、
/// 相机序列号、作者、注释、内嵌缩略图；保留拍摄时间、方向、DPI 和颜色配置。
/// 查看不输出；清理 <c>{原名}_已清理.{原扩展名}</c>。
/// </summary>
public sealed class ImageExifHandler : ImageToolHandlerBase
{
    /// <summary>
    /// Tags preserved by 清理 (frozen matrix): 拍摄时间、方向、DPI。颜色配置走
    /// ICC profile（单独保留），不在此列。
    /// </summary>
    private static readonly HashSet<ushort> KeepTags = new()
    {
        (ushort)ExifTag.DateTimeOriginal,
        (ushort)ExifTag.DateTimeDigitized,
        (ushort)ExifTag.DateTime,
        (ushort)ExifTag.Orientation,
        (ushort)ExifTag.XResolution,
        (ushort)ExifTag.YResolution,
        (ushort)ExifTag.ResolutionUnit,
        (ushort)ExifTag.ColorSpace,
        (ushort)ExifTag.PixelXDimension,
        (ushort)ExifTag.PixelYDimension,
    };

    public override string ToolId => "image.exif";

    protected override string OutputSuffix => "_已清理";

    protected override IReadOnlyList<JobError> ValidateParameters(JobRequest request)
    {
        string mode = TryGetString(request.Parameters, "mode") ?? "view";
        if (!mode.Equals("view", StringComparison.OrdinalIgnoreCase) &&
            !mode.Equals("clean", StringComparison.OrdinalIgnoreCase))
        {
            return new[]
            {
                new JobError("image.exif.mode-unsupported", "模式必须是 view 或 clean。", false),
            };
        }

        return Array.Empty<JobError>();
    }

    protected override async Task<IReadOnlyList<string>> ProcessInputAsync(
        string input,
        JobExecutionContext context,
        CancellationToken cancellationToken)
    {
        string mode = TryGetString(context.Request.Parameters, "mode") ?? "view";
        if (mode.Equals("view", StringComparison.OrdinalIgnoreCase))
        {
            // 只看：不产生输出。界面经 ImageExifReader 查询分组字段。
            return Array.Empty<string>();
        }

        return await base.ProcessInputAsync(input, context, cancellationToken).ConfigureAwait(false);
    }

    protected override Task ApplyAsync(
        MagickImage image,
        JobRequest request,
        CancellationToken cancellationToken)
    {
        IExifProfile? profile = image.GetExifProfile();
        if (profile is not null)
        {
            foreach (IExifValue value in profile.Values.ToArray())
            {
                if (value?.Tag is ExifTag tag && !KeepTags.Contains((ushort)tag))
                {
                    profile.RemoveValue(tag);
                }
            }

            // 内嵌缩略图单独移除（含其 offset/length 对）。
            profile.RemoveThumbnail();
            image.SetProfile(profile);
        }

        // IPTC/XMP/8BIM/comment 携带作者与注释；ICC 颜色配置保留。
        image.RemoveProfile("iptc");
        image.RemoveProfile("xmp");
        image.RemoveProfile("8bim");
        image.RemoveProfile("comment");
        image.RemoveProfile("app13");

        return Task.CompletedTask;
    }

    /// <summary>
    /// Read-only EXIF summary used by the UI (分组: 基础/拍摄/位置/设备) and by the
    /// smoke harness. Never writes anything.
    /// </summary>
    public static IReadOnlyList<(string Group, string Name, string Value)> ReadSummary(string path)
    {
        var rows = new List<(string, string, string)>();
        using var image = new MagickImage(path);
        IExifProfile? profile = image.GetExifProfile();
        if (profile is null)
        {
            return rows;
        }

        foreach (IExifValue value in profile.Values)
        {
            if (value?.Tag is not ExifTag tag)
            {
                continue;
            }

            string name = tag.ToString();
            rows.Add((GroupFor(name, (ushort)tag), name, value.ToString() ?? string.Empty));
        }

        return rows;
    }

    /// <summary>
    /// UI grouping. GPS tags are detected by their frozen "GPS*" name prefix
    /// (every GPS IFD member follows it) and, as a belt-and-braces check, by the
    /// 0x0000–0x001F id range that only the GPS IFD uses. Camera/曝光 tags are
    /// matched by name because their ids overlap the base IFD.
    /// </summary>
    private static string GroupFor(string name, ushort tag)
    {
        if (IsGps(name, tag))
        {
            return "位置";
        }

        if (IsCamera(name))
        {
            return "设备";
        }

        if (IsExposure(name))
        {
            return "拍摄";
        }

        return "基础";
    }

    private static bool IsGps(string name, ushort tag) =>
        name.StartsWith("GPS", StringComparison.OrdinalIgnoreCase) || tag <= 0x001F;

    private static bool IsCamera(string name) =>
        name.Contains("Make", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Model", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Serial", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Software", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Lens", StringComparison.OrdinalIgnoreCase);

    private static bool IsExposure(string name) =>
        name.Contains("Exposure", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("FNumber", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("ISO", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Focal", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("DateTime", StringComparison.OrdinalIgnoreCase);
}
