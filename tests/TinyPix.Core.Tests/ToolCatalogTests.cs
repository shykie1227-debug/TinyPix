using TinyPix.Core.Tools;

namespace TinyPix.Core.Tests;

public sealed class ToolCatalogTests
{
    [Fact]
    public void Catalog_contains_all_50_unique_frozen_tool_ids()
    {
        Assert.Equal(50, ToolCatalog.All.Count);
        Assert.Equal(50, ToolCatalog.All.Select(tool => tool.Id).Distinct().Count());
    }

    [Fact]
    public void Catalog_excludes_archive_compression_tools()
    {
        Assert.DoesNotContain(ToolCatalog.All, tool => tool.Id == "file.zip");
        Assert.DoesNotContain(ToolCatalog.All, tool => tool.Id == "file.duplicates");
    }

    [Fact]
    public void Video_time_trim_is_a_timeline_tool()
    {
        ToolDescriptor descriptor = ToolCatalog.GetRequired("video.trim");

        Assert.Equal("视频时间剪辑", descriptor.DisplayName);
        Assert.Equal(ToolCategory.Video, descriptor.Category);
        Assert.Equal(ToolTemplate.Timeline, descriptor.Template);
        Assert.False(descriptor.SupportsBatch);
    }

    [Theory]
    [InlineData("video.convert", ToolCategory.Video, ToolTemplate.Conversion)]
    [InlineData("idphoto.create", ToolCategory.IdPhoto, ToolTemplate.IdPhoto)]
    [InlineData("pdf.merge", ToolCategory.Pdf, ToolTemplate.PdfPages)]
    [InlineData("ocr.image", ToolCategory.Recognition, ToolTemplate.OcrAndCode)]
    [InlineData("capture.record", ToolCategory.Toolbox, ToolTemplate.GeneratorAndFiles)]
    [InlineData("settings.general", ToolCategory.Settings, ToolTemplate.Settings)]
    public void Catalog_exposes_stable_category_and_template(
        string id,
        ToolCategory category,
        ToolTemplate template)
    {
        ToolDescriptor descriptor = ToolCatalog.GetRequired(id);

        Assert.Equal(category, descriptor.Category);
        Assert.Equal(template, descriptor.Template);
    }

    [Fact]
    public void Unknown_tool_id_is_rejected()
    {
        Assert.Throws<KeyNotFoundException>(() => ToolCatalog.GetRequired("cloud.magic"));
    }
}
