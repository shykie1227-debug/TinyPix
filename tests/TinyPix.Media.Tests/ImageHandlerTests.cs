using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImageMagick;
using TinyPix.Core.Jobs;
using TinyPix.Core.Tools;
using TinyPix.Media.Images;
using Xunit;

namespace TinyPix.Media.Tests;

/// <summary>
/// Contract tests for the Phase 3 image workbench handlers (design/FEATURE-MATRIX
/// §4). These execute the real Magick.NET pipeline, so they run on the Windows
/// build agent (the macOS Intel box ships no Magick native binary); the same
/// suite gates the VM publish. Every test writes into its own temp directory.
/// </summary>
public sealed class ImageHandlerTests : IDisposable
{
    // Q16 quantum: 0–65535. 8-bit v maps to v * 257.
    private const int Quantum8 = 257;

    private readonly string _root;

    public ImageHandlerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "tinypix-image-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch
        {
            // Temp cleanup is best effort.
        }
    }

    // ---- helpers ------------------------------------------------------------

    private JobRequest Request(
        string toolId, IReadOnlyList<string> inputs, Dictionary<string, string>? parameters = null) =>
        JobRequest.Create(toolId, inputs, _root, JobWorkload.HeavyMedia, parameters);

    private static JobExecutionContext Context(JobRequest request) =>
        new(request, new Progress<JobProgress>(_ => { }));

    /// <summary>Writes a solid-colour test image and returns its path.</summary>
    private async Task<string> WriteImageAsync(
        string name, int width, int height, MagickColor color, MagickFormat? format = null)
    {
        string path = Path.Combine(_root, name);
        using var image = new MagickImage(color, (uint)width, (uint)height);
        image.Format = format ?? MagickFormat.Png;
        await image.WriteAsync(path);
        return path;
    }

    /// <summary>White canvas with a black square in the middle (border = background).</summary>
    private async Task<string> WriteSubjectOnWhiteAsync(string name, int size)
    {
        string path = Path.Combine(_root, name);
        using var image = new MagickImage(MagickColors.White, (uint)size, (uint)size);
        var drawables = new ImageMagick.Drawing.Drawables();
        drawables.FillColor(MagickColors.Black);
        drawables.Rectangle(size * 0.25, size * 0.25, size * 0.75, size * 0.75);
        image.Draw(drawables);
        image.Format = MagickFormat.Png;
        await image.WriteAsync(path);
        return path;
    }

    /// <summary>Writes a JPEG carrying GPS + camera + capture EXIF.</summary>
    private async Task<string> WriteExifJpegAsync(string name)
    {
        string path = Path.Combine(_root, name);
        using var image = new MagickImage(MagickColors.Blue, 80, 60);
        var profile = new ExifProfile();
        profile.SetValue<string>(ExifTag.Make, "ProbeCam");
        profile.SetValue<string>(ExifTag.Model, "X1");
        profile.SetValue<string>(ExifTag.DateTimeOriginal, "2026:10:16 12:00:00");
        profile.SetValue<string>(ExifTag.Software, "ProbeSuite");
        profile.SetValue<byte[]>(ExifTag.GPSVersionID, new byte[] { 2, 2, 0, 0 });
        profile.SetValue<Rational[]>(
            ExifTag.GPSLatitude, new[] { new Rational(31.0), new Rational(12.0), new Rational(36.0) });
        profile.SetValue<Rational[]>(
            ExifTag.GPSLongitude, new[] { new Rational(121.0), new Rational(28.0), new Rational(12.0) });
        image.SetProfile(profile);
        image.Format = MagickFormat.Jpeg;
        await image.WriteAsync(path);
        return path;
    }

    private static async Task<ToolExecutionResult> RunAsync(
        IToolHandler handler, JobRequest request)
    {
        ToolValidationResult validation = await handler.ValidateAsync(request, CancellationToken.None);
        Assert.True(
            validation.IsValid,
            $"validation failed: {string.Join("; ", validation.Errors.Select(e => $"{e.Code}:{e.Message}"))}");
        return await handler.ExecuteAsync(Context(request), CancellationToken.None);
    }

    private static MagickImage Load(string path) => new(path);

    // ---- image.resize -------------------------------------------------------

    [Fact]
    public async Task Resize_WidthOnly_KeepsAspectAndSuffix()
    {
        string input = await WriteImageAsync("resize-src.png", 200, 100, MagickColors.Red);
        var handler = new ImageResizeHandler();

        ToolExecutionResult result = await RunAsync(handler, Request("image.resize", new[] { input },
            new() { ["width"] = "50" }));

        string output = Assert.Single(result.OutputPaths);
        Assert.EndsWith("resize-src_缩放.png", output);
        using MagickImage image = Load(output);
        Assert.Equal(50u, image.Width);
        Assert.Equal(25u, image.Height);
    }

    [Fact]
    public async Task Resize_NoEnlarge_KeepsSourceSize()
    {
        string input = await WriteImageAsync("resize-small.png", 120, 80, MagickColors.Green);
        var handler = new ImageResizeHandler();

        ToolExecutionResult result = await RunAsync(handler, Request("image.resize", new[] { input },
            new() { ["width"] = "400", ["noEnlarge"] = "1" }));

        using MagickImage image = Load(Assert.Single(result.OutputPaths));
        Assert.Equal(120u, image.Width);
        Assert.Equal(80u, image.Height);
    }

    [Fact]
    public async Task Resize_RejectsMissingSize()
    {
        string input = await WriteImageAsync("resize-none.png", 120, 80, MagickColors.Green);
        var handler = new ImageResizeHandler();

        ToolValidationResult validation =
            await handler.ValidateAsync(Request("image.resize", new[] { input }), CancellationToken.None);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Code == "image.resize.missing-size");
    }

    // ---- image.crop ---------------------------------------------------------

    [Fact]
    public async Task Crop_RatioOneOne_CentresSquare()
    {
        string input = await WriteImageAsync("crop-src.png", 200, 100, MagickColors.Blue);
        var handler = new ImageCropHandler();

        ToolExecutionResult result = await RunAsync(handler, Request("image.crop", new[] { input },
            new() { ["ratio"] = "1:1" }));

        string output = Assert.Single(result.OutputPaths);
        Assert.EndsWith("crop-src_裁切.png", output);
        using MagickImage image = Load(output);
        Assert.Equal(100u, image.Width);
        Assert.Equal(100u, image.Height);
    }

    [Fact]
    public async Task Crop_ExplicitRect_UsesGivenGeometry()
    {
        string input = await WriteImageAsync("crop-rect.png", 200, 100, MagickColors.Blue);
        var handler = new ImageCropHandler();

        ToolExecutionResult result = await RunAsync(handler, Request("image.crop", new[] { input },
            new() { ["x"] = "10", ["y"] = "5", ["width"] = "60", ["height"] = "40" }));

        using MagickImage image = Load(Assert.Single(result.OutputPaths));
        Assert.Equal(60u, image.Width);
        Assert.Equal(40u, image.Height);
    }

    // ---- image.transform ----------------------------------------------------

    [Fact]
    public async Task Transform_Rotate90_SwapsAxes()
    {
        string input = await WriteImageAsync("rot-src.png", 200, 100, MagickColors.Red);
        var handler = new ImageTransformHandler();

        ToolExecutionResult result = await RunAsync(handler, Request("image.transform", new[] { input },
            new() { ["rotate"] = "90" }));

        string output = Assert.Single(result.OutputPaths);
        Assert.EndsWith("rot-src_旋转.png", output);
        using MagickImage image = Load(output);
        Assert.Equal(100u, image.Width);
        Assert.Equal(200u, image.Height);
    }

    // ---- image.convert ------------------------------------------------------

    [Fact]
    public async Task Convert_PngToJpg_ChangesExtension()
    {
        string input = await WriteImageAsync("conv-src.png", 64, 48, MagickColors.Orange);
        var handler = new ImageConvertHandler();

        ToolExecutionResult result = await RunAsync(handler, Request("image.convert", new[] { input },
            new() { ["format"] = "jpg" }));

        string output = Assert.Single(result.OutputPaths);
        Assert.EndsWith("conv-src_转换.jpg", output);
        Assert.Equal(MagickFormat.Jpeg, Load(output).Format);
    }

    [Fact]
    public async Task Convert_MultiPageTiff_EmitsNumberedSequence()
    {
        string input = Path.Combine(_root, "multi.tif");
        using (var collection = new MagickImageCollection())
        {
            collection.Add(new MagickImage(MagickColors.Red, 32, 32));
            collection.Add(new MagickImage(MagickColors.Green, 32, 32));
            collection.Add(new MagickImage(MagickColors.Blue, 32, 32));
            await collection.WriteAsync(input);
        }

        var handler = new ImageConvertHandler();
        ToolExecutionResult result = await RunAsync(handler, Request("image.convert", new[] { input },
            new() { ["format"] = "png" }));

        Assert.Equal(3, result.OutputPaths.Count);
        Assert.Contains(result.OutputPaths, p => p.EndsWith("multi_页_001.png"));
        Assert.Contains(result.OutputPaths, p => p.EndsWith("multi_页_002.png"));
        Assert.Contains(result.OutputPaths, p => p.EndsWith("multi_页_003.png"));
    }

    // ---- image.compress -----------------------------------------------------

    [Fact]
    public async Task Compress_LowQuality_BeatsHighQuality()
    {
        string source = Path.Combine(_root, "gradient.png");
        using (var image = new MagickImage(MagickColors.White, 256, 256))
        {
            var drawables = new ImageMagick.Drawing.Drawables();
            drawables.FillColor(MagickColors.Black);
            for (int y = 0; y < 256; y += 8)
            {
                drawables.Rectangle(0, y, 256, y + 4);
            }

            image.Draw(drawables);
            image.Format = MagickFormat.Png;
            await image.WriteAsync(source);
        }

        var handler = new ImageCompressHandler();
        ToolExecutionResult low = await RunAsync(handler, Request("image.compress", new[] { source },
            new() { ["quality"] = "20", ["format"] = "jpg" }));
        ToolExecutionResult high = await RunAsync(handler, Request("image.compress", new[] { source },
            new() { ["quality"] = "95", ["format"] = "jpg" }));

        long lowBytes = new FileInfo(Assert.Single(low.OutputPaths)).Length;
        long highBytes = new FileInfo(Assert.Single(high.OutputPaths)).Length;
        Assert.True(lowBytes < highBytes, $"quality 20 ({lowBytes} B) should beat quality 95 ({highBytes} B)");
    }

    [Fact]
    public async Task Compress_TransparentPng_ToJpg_FlattensOnWhite()
    {
        string input = Path.Combine(_root, "alpha.png");
        using (var image = new MagickImage(MagickColors.Transparent, 64, 64))
        {
            var drawables = new ImageMagick.Drawing.Drawables();
            drawables.FillColor(MagickColors.Red);
            drawables.Rectangle(16, 16, 48, 48);
            image.Draw(drawables);
            image.Format = MagickFormat.Png;
            await image.WriteAsync(input);
        }

        var handler = new ImageCompressHandler();
        ToolExecutionResult result = await RunAsync(handler, Request("image.compress", new[] { input },
            new() { ["quality"] = "80", ["format"] = "jpg" }));

        using MagickImage flat = Load(Assert.Single(result.OutputPaths));
        Assert.False(flat.HasAlpha);
        IMagickColor<ushort>? corner = flat.GetPixels().GetPixel(0, 0).ToColor();
        Assert.NotNull(corner);
        Assert.Equal(ushort.MaxValue, corner!.R);
        Assert.Equal(ushort.MaxValue, corner.G);
        Assert.Equal(ushort.MaxValue, corner.B);
    }

    // ---- image.color --------------------------------------------------------

    [Fact]
    public async Task Color_BrightnessUp_RaisesPixelValue()
    {
        // MagickColor(int,int,int) takes QUANTUM (0-65535) values, so build the
        // source from hex to get a real mid grey.
        string input = await WriteImageAsync("color-src.png", 64, 64, new MagickColor("#505050"));
        var handler = new ImageColorHandler();

        ToolExecutionResult result = await RunAsync(handler, Request("image.color", new[] { input },
            new() { ["brightness"] = "50" }));

        using MagickImage image = Load(Assert.Single(result.OutputPaths));
        IMagickColor<ushort>? pixel = image.GetPixels().GetPixel(32, 32).ToColor();
        Assert.NotNull(pixel);
        // BrightnessContrast lifts towards white: v' = v + p*(max - v).
        // 0x50 (20560) + 50% of the remaining headroom ≈ 43047 (~168/255).
        Assert.True(pixel!.R > 150 * Quantum8, $"brightened red {pixel.R} should exceed 150/255");
    }

    [Fact]
    public async Task Color_RejectsOutOfRangeValues()
    {
        string input = await WriteImageAsync("color-range.png", 32, 32, MagickColors.Gray);
        var handler = new ImageColorHandler();

        ToolValidationResult validation =
            await handler.ValidateAsync(Request("image.color", new[] { input },
                new() { ["saturation"] = "150" }), CancellationToken.None);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Code == "image.color.saturation-out-of-range");
    }

    // ---- image.watermark ----------------------------------------------------

    [Fact]
    public async Task Watermark_TextDefault_ChangesPixels()
    {
        string input = await WriteImageAsync("wm-src.png", 320, 240, MagickColors.White);
        var handler = new ImageWatermarkHandler();

        ToolExecutionResult result = await RunAsync(handler, Request("image.watermark", new[] { input }));

        string output = Assert.Single(result.OutputPaths);
        Assert.EndsWith("wm-src_水印.png", output);
        using MagickImage source = Load(input);
        using MagickImage marked = Load(output);
        Assert.True(Differ(source, marked), "the default text watermark must alter pixels");
    }

    [Fact]
    public async Task Watermark_Tile_ChangesPixels()
    {
        string input = await WriteImageAsync("wm-tile.png", 320, 240, MagickColors.White);
        var handler = new ImageWatermarkHandler();

        ToolExecutionResult result = await RunAsync(handler, Request("image.watermark", new[] { input },
            new() { ["text"] = "机密", ["tile"] = "1", ["opacity"] = "50" }));

        using MagickImage source = Load(input);
        using MagickImage marked = Load(Assert.Single(result.OutputPaths));
        Assert.True(Differ(source, marked), "the tiled watermark must alter pixels");
    }

    [Fact]
    public async Task Watermark_ImageMark_KeepsSemiTransparency()
    {
        // A 50%-transparent PNG mark: compositing it must stay a blend, not a
        // solid stamp (regression: AlphaAction.Set used to flatten it).
        string mark = Path.Combine(_root, "mark.png");
        using (var markImage = new MagickImage(new MagickColor(255, 0, 0, 32768), 64, 64))
        {
            markImage.Format = MagickFormat.Png;
            await markImage.WriteAsync(mark);
        }

        string input = await WriteImageAsync("wm-image.png", 320, 240, MagickColors.White);
        var handler = new ImageWatermarkHandler();

        ToolExecutionResult result = await RunAsync(handler, Request("image.watermark", new[] { input },
            new() { ["watermark"] = mark, ["opacity"] = "100", ["position"] = "topleft" }));

        using MagickImage marked = Load(Assert.Single(result.OutputPaths));
        IMagickColor<ushort>? stamped = marked.GetPixels().GetPixel(16, 16).ToColor();
        Assert.NotNull(stamped);
        // White under 50% red lands near (255, 128, 128) in 8-bit terms.
        Assert.True(stamped!.R > stamped.G + 8 * Quantum8, $"red channel {stamped.R} should dominate green {stamped.G}");
        Assert.True(stamped.G > 8 * Quantum8, $"green {stamped.G} should be lifted by the blend");
        Assert.True(stamped.B > 8 * Quantum8, $"blue {stamped.B} should be lifted by the blend");
    }

    [Fact]
    public async Task Watermark_RejectsMissingWatermarkFile()
    {
        string input = await WriteImageAsync("wm-missing.png", 64, 64, MagickColors.White);
        var handler = new ImageWatermarkHandler();

        ToolValidationResult validation =
            await handler.ValidateAsync(Request("image.watermark", new[] { input },
                new() { ["watermark"] = Path.Combine(_root, "nope.png") }), CancellationToken.None);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Code == "image.watermark.missing");
    }

    // ---- image.exif ---------------------------------------------------------

    [Fact]
    public async Task Exif_Clean_RemovesGpsAndKeepsDateTime()
    {
        string input = await WriteExifJpegAsync("exif.jpg");
        var handler = new ImageExifHandler();

        ToolExecutionResult result = await RunAsync(handler, Request("image.exif", new[] { input },
            new() { ["mode"] = "clean" }));

        string output = Assert.Single(result.OutputPaths);
        Assert.EndsWith("exif_已清理.jpg", output);

        using MagickImage cleaned = Load(output);
        IExifProfile? profile = cleaned.GetExifProfile();
        Assert.NotNull(profile);

        HashSet<string> remaining = profile!.Values
            .Where(v => v?.Tag is not null)
            .Select(v => v.Tag.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain(remaining, n => n.StartsWith("GPS", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("DateTimeOriginal", remaining);
        Assert.DoesNotContain("Software", remaining);
        Assert.DoesNotContain("Make", remaining);
    }

    [Fact]
    public async Task Exif_View_ProducesNoOutput()
    {
        string input = await WriteExifJpegAsync("exif-view.jpg");
        var handler = new ImageExifHandler();

        ToolExecutionResult result = await RunAsync(handler, Request("image.exif", new[] { input },
            new() { ["mode"] = "view" }));

        Assert.Empty(result.OutputPaths);
    }

    [Fact]
    public async Task Exif_ReadSummary_GroupsGpsSeparately()
    {
        string input = await WriteExifJpegAsync("exif-summary.jpg");

        IReadOnlyList<(string Group, string Name, string Value)> rows =
            ImageExifHandler.ReadSummary(input);

        Assert.Contains(rows, r => r.Group == "位置" && r.Name.StartsWith("GPS", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(rows, r => r.Group == "设备" && r.Name is "Make" or "Model" or "Software");
        Assert.Contains(rows, r => r.Group == "拍摄" && r.Name is "DateTimeOriginal");
    }

    // ---- image.collage ------------------------------------------------------

    [Fact]
    public async Task Collage_ContactSheet_IsA4Portrait300Dpi()
    {
        string a = await WriteImageAsync("sheet-a.png", 800, 600, MagickColors.Red);
        string b = await WriteImageAsync("sheet-b.png", 600, 800, MagickColors.Blue);
        string c = await WriteImageAsync("sheet-c.png", 500, 500, MagickColors.Green);
        var handler = new ImageCollageHandler();

        ToolExecutionResult result = await RunAsync(handler, Request("image.collage", new[] { a, b, c },
            new() { ["mode"] = "contact" }));

        string output = Assert.Single(result.OutputPaths);
        Assert.EndsWith("sheet-a_联系表_001.jpg", output);
        using MagickImage sheet = Load(output);
        Assert.Equal(2480u, sheet.Width);
        Assert.Equal(3508u, sheet.Height);
    }

    [Fact]
    public async Task Collage_RejectsSingleImage()
    {
        string only = await WriteImageAsync("collage-one.png", 64, 64, MagickColors.Red);
        var handler = new ImageCollageHandler();

        ToolValidationResult validation =
            await handler.ValidateAsync(Request("image.collage", new[] { only }), CancellationToken.None);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Code == "image.collage.too-few");
    }

    // ---- image.batch --------------------------------------------------------

    [Fact]
    public async Task Batch_ChainCropResizeColor_AppliesEveryStep()
    {
        string input = await WriteImageAsync("batch-src.png", 400, 400, new MagickColor("#505050"));
        var handler = new ImageBatchHandler();

        ToolExecutionResult result = await RunAsync(handler, Request("image.batch", new[] { input }, new()
        {
            ["ratio"] = "1:1",
            ["width"] = "100",
            ["brightness"] = "40",
        }));

        string output = Assert.Single(result.OutputPaths);
        Assert.EndsWith("batch-src_批量.png", output);
        using MagickImage image = Load(output);
        Assert.Equal(100u, image.Width);
        Assert.Equal(100u, image.Height);
        IMagickColor<ushort>? pixel = image.GetPixels().GetPixel(50, 50).ToColor();
        Assert.NotNull(pixel);
        // 0x50 (20560) + 40% headroom ≈ 38550 (~150/255).
        Assert.True(pixel!.R > 140 * Quantum8, $"brightened red {pixel.R} should exceed 140/255");
    }

    [Fact]
    public async Task Batch_FormatOnly_ConvertsWithoutPixelSteps()
    {
        string input = await WriteImageAsync("batch-fmt.png", 64, 64, MagickColors.Purple);
        var handler = new ImageBatchHandler();

        ToolExecutionResult result = await RunAsync(handler, Request("image.batch", new[] { input },
            new() { ["format"] = "webp" }));

        string output = Assert.Single(result.OutputPaths);
        Assert.EndsWith("batch-fmt_批量.webp", output);
        Assert.Equal(MagickFormat.WebP, Load(output).Format);
    }

    [Fact]
    public async Task Batch_RejectsEmptyChain()
    {
        string input = await WriteImageAsync("batch-empty.png", 64, 64, MagickColors.Pink);
        var handler = new ImageBatchHandler();

        ToolValidationResult validation =
            await handler.ValidateAsync(Request("image.batch", new[] { input }), CancellationToken.None);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Code == "image.batch.no-steps");
    }

    [Fact]
    public async Task Batch_WatermarkStep_ChangesPixels()
    {
        string input = await WriteImageAsync("batch-wm.png", 320, 240, MagickColors.White);
        var handler = new ImageBatchHandler();

        ToolExecutionResult result = await RunAsync(handler, Request("image.batch", new[] { input },
            new() { ["text"] = "TinyPix" }));

        using MagickImage source = Load(input);
        using MagickImage marked = Load(Assert.Single(result.OutputPaths));
        Assert.True(Differ(source, marked), "the batch watermark step must alter pixels");
    }

    // ---- image.background ---------------------------------------------------

    [Fact]
    public async Task Background_Remove_ClearsSolidBackgroundCorner()
    {
        string input = await WriteSubjectOnWhiteAsync("bg-remove.png", 200);
        var handler = new ImageBackgroundHandler();

        ToolExecutionResult result = await RunAsync(handler, Request("image.background", new[] { input },
            new() { ["mode"] = "remove", ["fuzz"] = "20", ["format"] = "png" }));

        string output = Assert.Single(result.OutputPaths);
        Assert.EndsWith("bg-remove_背景.png", output);
        using MagickImage image = Load(output);
        Assert.True(image.HasAlpha, "removal must keep an alpha channel when the format allows it");
        IMagickColor<ushort>? corner = image.GetPixels().GetPixel(0, 0).ToColor();
        Assert.NotNull(corner);
        Assert.True(corner!.A < 4096, $"corner alpha {corner.A} should be near-transparent");
    }

    [Fact]
    public async Task Background_Replace_FillsWithSolidColour()
    {
        string input = await WriteSubjectOnWhiteAsync("bg-replace.png", 200);
        var handler = new ImageBackgroundHandler();

        ToolExecutionResult result = await RunAsync(handler, Request("image.background", new[] { input }, new()
        {
            ["mode"] = "replace",
            ["fuzz"] = "20",
            ["color"] = "#102030",
            ["format"] = "png",
        }));

        using MagickImage image = Load(Assert.Single(result.OutputPaths));
        IMagickColor<ushort>? corner = image.GetPixels().GetPixel(0, 0).ToColor();
        Assert.NotNull(corner);
        Assert.InRange(corner!.R, 0x10 * Quantum8 - 4, 0x10 * Quantum8 + 4);
        Assert.InRange(corner.G, 0x20 * Quantum8 - 4, 0x20 * Quantum8 + 4);
        Assert.InRange(corner.B, 0x30 * Quantum8 - 4, 0x30 * Quantum8 + 4);
    }

    [Fact]
    public async Task Background_RejectsUnknownMode()
    {
        string input = await WriteImageAsync("bg-mode.png", 32, 32, MagickColors.White);
        var handler = new ImageBackgroundHandler();

        ToolValidationResult validation =
            await handler.ValidateAsync(Request("image.background", new[] { input },
                new() { ["mode"] = "magic" }), CancellationToken.None);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Code == "image.background.mode-unsupported");
    }

    // ---- shared rules -------------------------------------------------------

    [Fact]
    public async Task Output_Collision_AppendsCounter()
    {
        string input = await WriteImageAsync("conflict.png", 64, 64, MagickColors.Red);
        var handler = new ImageResizeHandler();
        string first = ImageOutputNaming.BuildOutputPath(input, "_缩放", "png", _root);
        await File.WriteAllTextAsync(first, "occupied");

        ToolExecutionResult result = await RunAsync(handler, Request("image.resize", new[] { input },
            new() { ["width"] = "32" }));

        string output = Assert.Single(result.OutputPaths);
        Assert.EndsWith("conflict_缩放 (1).png", output);
        Assert.NotEqual(first, output);
    }

    [Fact]
    public async Task AnyTool_RejectsUnsupportedExtension()
    {
        string bogus = Path.Combine(_root, "photo.raw");
        await File.WriteAllTextAsync(bogus, "not an image");
        var handler = new ImageResizeHandler();

        ToolValidationResult validation =
            await handler.ValidateAsync(Request("image.resize", new[] { bogus },
                new() { ["width"] = "10" }), CancellationToken.None);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Code == "image.input.unsupported");
    }

    [Fact]
    public async Task AnyTool_SingleFailure_ContinuesBatch()
    {
        string good = await WriteImageAsync("partial-good.png", 64, 64, MagickColors.Red);
        string bad = Path.Combine(_root, "broken.png");
        await File.WriteAllTextAsync(bad, "this is not a png");

        var handler = new ImageResizeHandler();
        ToolExecutionResult result = await RunAsync(handler, Request("image.resize", new[] { good, bad },
            new() { ["width"] = "32" }));

        Assert.Single(result.OutputPaths);
        Assert.NotNull(result.PartialFailure);
        Assert.EndsWith("partial-good_缩放.png", result.OutputPaths[0]);
    }

    // ---- pixel helpers ------------------------------------------------------

    private static bool Differ(MagickImage a, MagickImage b)
    {
        if (a.Width != b.Width || a.Height != b.Height)
        {
            return true;
        }

        double distortion = a.Compare(b, ErrorMetric.RootMeanSquared);
        return distortion > 0.0001;
    }
}
