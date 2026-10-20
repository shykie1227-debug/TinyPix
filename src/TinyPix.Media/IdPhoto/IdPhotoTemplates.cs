using System;
using System.Collections.Generic;
using System.Linq;

namespace TinyPix.Media.IdPhoto;

/// <summary>
/// 证件照模板（FEATURE-MATRIX §5.1 冻结）：每个 presetId 定义画幅毫米、
/// 默认像素、DPI、背景色、头部/眼位约束（如有）与官方来源。
/// 输出像素 = 毫米 ÷ 25.4 × DPI（四舍五入），除 us-visa 用官方固定像素。
/// </summary>
public sealed record IdPhotoPreset(
    string PresetId,
    string DisplayName,
    double WidthMm,
    double HeightMm,
    int OutputWidthPx,
    int OutputHeightPx,
    int Dpi,
    string BackgroundDescription,
    IdPhotoHeadRules HeadRules,
    string SourceUrl,
    string VerificationDate);

/// <summary>头部/眼位约束（null = 该模板不设数字阈值，仅作软提示）。</summary>
public sealed record IdPhotoHeadRules(
    double? HeadWidthMinMm = null,
    double? HeadWidthMaxMm = null,
    double? ChinToTopMinMm = null,
    double? ChinToTopMaxMm = null,
    double? TopMarginMinMm = null,
    double? TopMarginMaxMm = null,
    double? ChinBelowMinMm = null,
    // Digital-mode head height ratio (us-visa: 50%-69%)
    double? HeadHeightRatioMin = null,
    double? HeadHeightRatioMax = null,
    // Eye-line-from-bottom ratio (us-visa: 56%-69%)
    double? EyeLineRatioMin = null,
    double? EyeLineRatioMax = null,
    // DE/face-height ratio 70-80%
    double? FaceHeightRatioMin = null,
    double? FaceHeightRatioMax = null);

public static class IdPhotoTemplates
{
    public static readonly IReadOnlyList<IdPhotoPreset> All = new[]
    {
        // 办公一寸（非官方证件）
        new IdPhotoPreset(
            "office-1in",
            "办公一寸（25×35 mm，非通用证件标准）",
            25, 35, 295, 413, 300,
            "白/蓝/红/自定义（按业务选择）",
            new IdPhotoHeadRules(),
            "https://hrss.wuxi.gov.cn/doc/2022/11/09/1761316.shtml",
            "2026-07-17"),

        // 办公二寸（非官方证件）
        new IdPhotoPreset(
            "office-2in",
            "办公二寸（35×49 mm，非通用证件标准）",
            35, 49, 413, 579, 300,
            "白/蓝/红/自定义（按业务选择）",
            new IdPhotoHeadRules(),
            "https://gat.hunan.gov.cn/gat/jwgk/zfxxgk/xxgkml/rsxx/202506/t20250612_33707473.html",
            "2026-07-17"),

        // 中国因私护照/旅行证
        new IdPhotoPreset(
            "cn-passport-travel",
            "中国因私护照／旅行证（33×48 mm）",
            33, 48, 390, 567, 300,
            "白底彩色",
            new IdPhotoHeadRules(
                HeadWidthMinMm: 15, HeadWidthMaxMm: 22,
                ChinToTopMinMm: 28, ChinToTopMaxMm: 33,
                TopMarginMinMm: 3, TopMarginMaxMm: 5,
                ChinBelowMinMm: 7),
            "https://pg.china-embassy.gov.cn/chn/lsyw/lszj/hzlxz/hzlzyq/",
            "2026-07-17"),

        // 美国签证（官方像素 600×600）
        new IdPhotoPreset(
            "us-visa",
            "美国签证照片（51×51 mm／600×600 px）",
            51, 51, 600, 600, 300,
            "白/米白",
            new IdPhotoHeadRules(
                HeadHeightRatioMin: 0.50, HeadHeightRatioMax: 0.69,
                EyeLineRatioMin: 0.56, EyeLineRatioMax: 0.69),
            "https://travel.state.gov/content/travel/en/us-visas/visa-information-resources/photos.html",
            "2026-07-17"),

        // 德国申根短期
        new IdPhotoPreset(
            "de-schengen-short",
            "德国申根签证（短期停留，35×45 mm）",
            35, 45, 413, 531, 300,
            "白底",
            new IdPhotoHeadRules(
                FaceHeightRatioMin: 0.70, FaceHeightRatioMax: 0.80),
            "https://china.diplo.de/cn-zh/service/visa-einreise/faq/faq-schengenvisa-1434980",
            "2026-07-17"),

        // 日本签证（中国境内申请）
        new IdPhotoPreset(
            "jp-visa-cn",
            "日本签证（中国境内申请，35×45 mm）",
            35, 45, 413, 531, 300,
            "白底",
            new IdPhotoHeadRules(), // 不设头高/头宽/眼位数字硬阈值
            "https://www.cn.emb-japan.go.jp/itpr_zh/visa_tankishoyo.html",
            "2026-07-17"),

        // 加拿大临时居民签证纸质照片
        new IdPhotoPreset(
            "ca-trv-paper",
            "加拿大临时居民签证纸质照片（最小35×45 mm）",
            35, 45, 413, 531, 300,
            "纯白/浅色；彩色默认",
            new IdPhotoHeadRules(
                ChinToTopMinMm: 31, ChinToTopMaxMm: 36),
            "https://www.canada.ca/en/immigration-refugees-citizenship/services/application/application-forms-guides/temporary-resident-visa-application-photograph-specifications.html",
            "2026-07-17"),
    };

    public static IdPhotoPreset? Find(string presetId) =>
        All.FirstOrDefault(p => p.PresetId.Equals(presetId, StringComparison.OrdinalIgnoreCase));
}
