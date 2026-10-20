using System;
using System.Collections.Generic;
using System.IO;

namespace TinyPix.Media.Pdf;

/// <summary>
/// PDF 工具冻结输出命名（FEATURE-MATRIX §6）：
/// merge → <c>{首个原名}_合并.pdf</c>；split → <c>{原名}_页_001.pdf</c>（序列）；
/// reorder → <c>{原名}_重排.pdf</c>；rotate → <c>{原名}_旋转.pdf</c>；
/// watermark → <c>{原名}_水印.pdf</c>；to-image → <c>{原名}_页_001.png</c>（序列）；
/// from-image → <c>{首图原名}_图片合成.pdf</c>；compress → <c>{原名}_压缩.pdf</c>。
/// 冲突统一 <c> (n)</c> 后缀（同 VideoOutputNaming）。
/// </summary>
public static class PdfNaming
{
    /// <summary>Single output with frozen suffix and collision suffix.</summary>
    public static string Build(string inputPath, string suffix, string outputDirectory)
    {
        string stem = Path.GetFileNameWithoutExtension(inputPath);
        string candidate = Path.Combine(outputDirectory, $"{stem}{suffix}.pdf");
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        int index = 1;
        string conflict;
        do
        {
            conflict = Path.Combine(outputDirectory, $"{stem}{suffix} ({index}).pdf");
            index++;
        }
        while (File.Exists(conflict));

        return conflict;
    }

    /// <summary>
    /// Sequence output <c>{stem}_{suffix}_{index:000}.{ext}</c>; the first collision
    /// bump applies to the whole batch so numbering stays contiguous.
    /// </summary>
    public static IReadOnlyList<string> BuildSequence(
        string inputPath,
        string suffix,
        string extension,
        int count,
        string outputDirectory)
    {
        string stem = Path.GetFileNameWithoutExtension(inputPath);
        int batchOffset = 0;
        while (File.Exists(Path.Combine(outputDirectory, Format(stem, suffix, extension, 1 + batchOffset))))
        {
            batchOffset++;
        }

        var paths = new string[count];
        for (int i = 0; i < count; i++)
        {
            paths[i] = Path.Combine(outputDirectory, Format(stem, suffix, extension, i + 1 + batchOffset));
        }

        return paths;
    }

    /// <summary>
    /// Sequence where the caller keeps adding files for the same source (split/to-image
    /// append to the same batch). <paramref name="startIndex"/> is 1-based.
    /// </summary>
    public static string SequencePath(
        string inputPath,
        string suffix,
        string extension,
        int index,
        string outputDirectory)
    {
        string stem = Path.GetFileNameWithoutExtension(inputPath);
        string path = Path.Combine(outputDirectory, Format(stem, suffix, extension, index));
        if (!File.Exists(path))
        {
            return path;
        }

        int bump = 1;
        string conflict;
        do
        {
            conflict = Path.Combine(outputDirectory, Format(stem, suffix, extension, index, bump));
            bump++;
        }
        while (File.Exists(conflict));

        return conflict;
    }

    private static string Format(string stem, string suffix, string extension, int index, int bump = 0) =>
        bump == 0
            ? $"{stem}_{suffix}_{index:000}.{extension}"
            : $"{stem}_{suffix}_{index:000} ({bump}).{extension}";
}
