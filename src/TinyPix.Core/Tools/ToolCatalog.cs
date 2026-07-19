using System.Collections.ObjectModel;
using TinyPix.Core.Jobs;

namespace TinyPix.Core.Tools;

public static class ToolCatalog
{
    private static readonly IReadOnlyDictionary<string, ToolDescriptor> ById =
        new ReadOnlyDictionary<string, ToolDescriptor>(Create().ToDictionary(tool => tool.Id));

    public static IReadOnlyCollection<ToolDescriptor> All { get; } =
        new ReadOnlyCollection<ToolDescriptor>(ById.Values.OrderBy(tool => tool.Id).ToList());

    public static ToolDescriptor GetRequired(string id) =>
        ById.TryGetValue(id, out ToolDescriptor? tool)
            ? tool
            : throw new KeyNotFoundException($"Unknown TinyPix tool id: {id}");

    private static IEnumerable<ToolDescriptor> Create()
    {
        yield return D("video.convert", "视频格式转换", ToolCategory.Video, ToolTemplate.Conversion, true, JobWorkload.HeavyMedia);
        yield return D("video.compress", "视频压缩", ToolCategory.Video, ToolTemplate.Conversion, true, JobWorkload.HeavyMedia);
        yield return D("video.gif", "GIF 制作", ToolCategory.Video, ToolTemplate.Timeline, false, JobWorkload.HeavyMedia);
        yield return D("video.trim", "视频时间剪辑", ToolCategory.Video, ToolTemplate.Timeline, false, JobWorkload.HeavyMedia);
        yield return D("video.split", "视频分段", ToolCategory.Video, ToolTemplate.Timeline, true, JobWorkload.HeavyMedia);
        yield return D("video.merge", "视频合并", ToolCategory.Video, ToolTemplate.Timeline, false, JobWorkload.HeavyMedia);
        yield return D("video.geometry", "裁切、缩放与旋转", ToolCategory.Video, ToolTemplate.Timeline, true, JobWorkload.HeavyMedia);
        yield return D("video.speed-volume", "速度与音量", ToolCategory.Video, ToolTemplate.Timeline, true, JobWorkload.HeavyMedia);
        yield return D("video.audio", "音频处理", ToolCategory.Video, ToolTemplate.Timeline, true, JobWorkload.HeavyMedia);
        yield return D("video.overlay", "水印与字幕", ToolCategory.Video, ToolTemplate.Timeline, true, JobWorkload.HeavyMedia);
        yield return D("video.frames", "截图与帧提取", ToolCategory.Video, ToolTemplate.Timeline, true, JobWorkload.HeavyMedia);
        yield return D("video.metadata", "媒体信息与元数据清理", ToolCategory.Video, ToolTemplate.Settings, true, JobWorkload.LightFile);

        yield return D("image.resize", "图片缩放", ToolCategory.Image, ToolTemplate.ImageEdit, true, JobWorkload.HeavyMedia);
        yield return D("image.crop", "图片裁切", ToolCategory.Image, ToolTemplate.ImageEdit, true, JobWorkload.HeavyMedia);
        yield return D("image.transform", "旋转与翻转", ToolCategory.Image, ToolTemplate.ImageEdit, true, JobWorkload.HeavyMedia);
        yield return D("image.convert", "图片格式转换", ToolCategory.Image, ToolTemplate.Conversion, true, JobWorkload.HeavyMedia);
        yield return D("image.compress", "图片压缩", ToolCategory.Image, ToolTemplate.Conversion, true, JobWorkload.HeavyMedia);
        yield return D("image.color", "亮度与色彩", ToolCategory.Image, ToolTemplate.ImageEdit, true, JobWorkload.HeavyMedia);
        yield return D("image.watermark", "图片水印", ToolCategory.Image, ToolTemplate.ImageEdit, true, JobWorkload.HeavyMedia);
        yield return D("image.exif", "EXIF 查看与清理", ToolCategory.Image, ToolTemplate.Settings, true, JobWorkload.LightFile);
        yield return D("image.collage", "拼图与联系表", ToolCategory.Image, ToolTemplate.ImageEdit, false, JobWorkload.HeavyMedia);
        yield return D("image.batch", "图片批量处理", ToolCategory.Image, ToolTemplate.ImageEdit, true, JobWorkload.HeavyMedia);
        yield return D("image.background", "人像背景移除与替换", ToolCategory.Image, ToolTemplate.ImageEdit, true, JobWorkload.HeavyMedia);

        yield return D("idphoto.create", "智能证件照", ToolCategory.IdPhoto, ToolTemplate.IdPhoto, true, JobWorkload.HeavyMedia);
        yield return D("idphoto.check", "证件照规格检查", ToolCategory.IdPhoto, ToolTemplate.IdPhoto, true, JobWorkload.HeavyMedia);
        yield return D("idphoto.print", "证件照排版", ToolCategory.IdPhoto, ToolTemplate.IdPhoto, true, JobWorkload.HeavyMedia);

        yield return D("pdf.merge", "PDF 合并", ToolCategory.Pdf, ToolTemplate.PdfPages, false, JobWorkload.HeavyMedia);
        yield return D("pdf.split", "PDF 拆分", ToolCategory.Pdf, ToolTemplate.PdfPages, true, JobWorkload.HeavyMedia);
        yield return D("pdf.reorder", "PDF 页面排序与删除", ToolCategory.Pdf, ToolTemplate.PdfPages, false, JobWorkload.HeavyMedia);
        yield return D("pdf.rotate", "PDF 页面旋转", ToolCategory.Pdf, ToolTemplate.PdfPages, true, JobWorkload.HeavyMedia);
        yield return D("pdf.watermark", "PDF 水印", ToolCategory.Pdf, ToolTemplate.PdfPages, true, JobWorkload.HeavyMedia);
        yield return D("pdf.to-image", "PDF 转图片", ToolCategory.Pdf, ToolTemplate.PdfPages, true, JobWorkload.HeavyMedia);
        yield return D("pdf.from-image", "图片转 PDF", ToolCategory.Pdf, ToolTemplate.PdfPages, false, JobWorkload.HeavyMedia);
        yield return D("pdf.compress", "PDF 压缩", ToolCategory.Pdf, ToolTemplate.Conversion, true, JobWorkload.HeavyMedia);

        yield return D("ocr.image", "图片文字识别", ToolCategory.Recognition, ToolTemplate.OcrAndCode, true, JobWorkload.HeavyMedia);
        yield return D("ocr.pdf", "扫描 PDF 文字识别", ToolCategory.Recognition, ToolTemplate.OcrAndCode, true, JobWorkload.HeavyMedia);
        yield return D("code.scan", "二维码与条码识别", ToolCategory.Recognition, ToolTemplate.OcrAndCode, true, JobWorkload.HeavyMedia);
        yield return D("code.generate", "二维码与条码生成", ToolCategory.Toolbox, ToolTemplate.GeneratorAndFiles, true, JobWorkload.LightFile);

        yield return D("capture.screenshot", "截图", ToolCategory.Toolbox, ToolTemplate.GeneratorAndFiles, false, JobWorkload.LightFile);
        yield return D("capture.record", "屏幕录制", ToolCategory.Toolbox, ToolTemplate.GeneratorAndFiles, false, JobWorkload.HeavyMedia);
        yield return D("color.picker", "屏幕取色器", ToolCategory.Toolbox, ToolTemplate.GeneratorAndFiles, false, JobWorkload.LightFile);
        yield return D("color.convert", "颜色值转换", ToolCategory.Toolbox, ToolTemplate.GeneratorAndFiles, true, JobWorkload.LightFile);
        yield return D("file.rename", "批量重命名副本", ToolCategory.Toolbox, ToolTemplate.GeneratorAndFiles, true, JobWorkload.LightFile);
        yield return D("file.hash", "文件校验值", ToolCategory.Toolbox, ToolTemplate.GeneratorAndFiles, true, JobWorkload.LightFile);
        yield return D("file.metadata", "文件与媒体信息", ToolCategory.Toolbox, ToolTemplate.Settings, true, JobWorkload.LightFile);
        yield return D("file.clipboard-image", "剪贴板图片保存", ToolCategory.Toolbox, ToolTemplate.GeneratorAndFiles, false, JobWorkload.LightFile);
        yield return D("file.batch-history", "任务历史与批处理队列", ToolCategory.Toolbox, ToolTemplate.Settings, true, JobWorkload.LightFile);

        yield return D("settings.general", "通用设置", ToolCategory.Settings, ToolTemplate.Settings, false, JobWorkload.LightFile);
        yield return D("settings.engines", "离线引擎与许可证", ToolCategory.Settings, ToolTemplate.Settings, false, JobWorkload.LightFile);
        yield return D("settings.cache", "缓存管理", ToolCategory.Settings, ToolTemplate.Settings, false, JobWorkload.LightFile);
    }

    private static ToolDescriptor D(
        string id,
        string displayName,
        ToolCategory category,
        ToolTemplate template,
        bool supportsBatch,
        JobWorkload workload) =>
        new(id, displayName, category, template, supportsBatch, workload);
}
