using System;
using System.Collections.Generic;
using System.Linq;

namespace TinyPix.Media.Pdf;

/// <summary>
/// 页码范围解析（FEATURE-MATRIX §6）：默认"全部"；接受 <c>1-3,5</c> 这类
/// 显式范围。解析结果页号升序且去重；越界在给定页数内校验。
/// </summary>
public static class PdfPageRanges
{
    /// <summary>
    /// Parse <paramref name="text"/> against a document of
    /// <paramref name="pageCount"/> pages. Empty/whitespace means "all pages".
    /// Returns null and sets <paramref name="error"/> when the spec is empty
    /// after parsing, contains out-of-range or malformed entries.
    /// </summary>
    public static IReadOnlyList<int>? Parse(string? text, int pageCount, out string? error)
    {
        error = null;
        if (pageCount <= 0)
        {
            error = "文档没有可用页面。";
            return null;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return Enumerable.Range(1, pageCount).ToArray();
        }

        var pages = new SortedSet<int>();
        foreach (string rawPart in text!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string part = rawPart.Trim();
            if (part.Length == 0)
            {
                continue;
            }

            int dash = part.IndexOf('-', StringComparison.Ordinal);
            if (dash < 0)
            {
                if (!int.TryParse(part, out int single))
                {
                    error = $"无法解析页码：{part}";
                    return null;
                }

                if (single < 1 || single > pageCount)
                {
                    error = $"页码 {single} 超出范围（共 {pageCount} 页）。";
                    return null;
                }

                pages.Add(single);
                continue;
            }

            string fromText = part[..dash].Trim();
            string toText = part[(dash + 1)..].Trim();
            if (!int.TryParse(fromText, out int from) || !int.TryParse(toText, out int to))
            {
                error = $"无法解析页码范围：{part}";
                return null;
            }

            if (from > to)
            {
                error = $"页码范围起点大于终点：{part}";
                return null;
            }

            if (from < 1 || to > pageCount)
            {
                error = $"页码范围 {part} 超出 1-{pageCount}。";
                return null;
            }

            for (int page = from; page <= to; page++)
            {
                pages.Add(page);
            }
        }

        if (pages.Count == 0)
        {
            error = "页码范围为空。";
            return null;
        }

        return pages.ToArray();
    }

    /// <summary>Frozen selectors for rotate: 当前/全部/奇数/偶数/明确页码.</summary>
    public static IReadOnlyList<int> Select(
        string? scope,
        IReadOnlyList<int> explicitPages,
        int currentPage,
        int pageCount,
        out string? error)
    {
        error = null;
        switch ((scope ?? "current").Trim().ToLowerInvariant())
        {
            case "current":
                if (currentPage < 1 || currentPage > pageCount)
                {
                    error = $"当前页 {currentPage} 超出范围（共 {pageCount} 页）。";
                    return Array.Empty<int>();
                }

                return new[] { currentPage };
            case "all":
                return Enumerable.Range(1, pageCount).ToArray();
            case "odd":
                return Enumerable.Range(1, pageCount).Where(page => page % 2 == 1).ToArray();
            case "even":
                return Enumerable.Range(1, pageCount).Where(page => page % 2 == 0).ToArray();
            case "explicit":
                foreach (int page in explicitPages)
                {
                    if (page < 1 || page > pageCount)
                    {
                        error = $"页码 {page} 超出范围（共 {pageCount} 页）。";
                        return Array.Empty<int>();
                    }
                }

                if (explicitPages.Count == 0)
                {
                    error = "未指定页码。";
                    return Array.Empty<int>();
                }

                return explicitPages.Distinct().OrderBy(page => page).ToArray();
            default:
                error = $"未知页码范围类型：{scope}（支持 current/all/odd/even/explicit）。";
                return Array.Empty<int>();
        }
    }
}
