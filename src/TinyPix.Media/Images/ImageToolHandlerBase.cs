using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TinyPix.Core.Jobs;
using TinyPix.Core.Tools;
using ImageMagick;

namespace TinyPix.Media.Images;

/// <summary>
/// Shared implementation for the 图片工作台 handlers (design/FEATURE-MATRIX.md §4).
/// Loads each input through Magick.NET (E3), normalises the EXIF orientation,
/// delegates the tool-specific pixels to <see cref="ApplyAsync"/>, resolves the
/// output format (keeping the source extension unless the tool converts), and
/// writes atomically. Per-file failures are collected instead of aborting the
/// batch, matching the frozen 单项失败继续 rule.
/// </summary>
public abstract class ImageToolHandlerBase : IToolHandler
{
    public abstract string ToolId { get; }

    /// <summary>Name suffix inserted before the extension, e.g. <c>_缩放</c>.</summary>
    protected abstract string OutputSuffix { get; }

    /// <summary>
    /// When non-null the tool always writes this format (image.convert); otherwise
    /// the source extension is kept (image.resize / crop / transform / color /
    /// watermark / batch-without-transcode).
    /// </summary>
    protected virtual string? ForcedOutputFormat => null;

    /// <summary>
    /// Quality default used when the caller omits the <c>quality</c> parameter:
    /// 90 for 转换 (frozen matrix), 82 for 压缩 (“标准” preset).
    /// </summary>
    protected virtual int DefaultQuality => 90;

    public virtual ValueTask<ToolValidationResult> ValidateAsync(
        JobRequest request,
        CancellationToken cancellationToken)
    {
        var errors = new List<JobError>();

        if (request.InputPaths.Count == 0)
        {
            errors.Add(new JobError("image.input.empty", "请至少添加一张图片。", false));
        }

        foreach (string input in request.InputPaths)
        {
            if (!File.Exists(input))
            {
                errors.Add(new JobError("image.input.missing", $"输入文件不存在：{input}", false));
                continue;
            }

            if (!ImageFormats.IsSupportedInput(input))
            {
                errors.Add(new JobError(
                    "image.input.unsupported",
                    $"不支持的图片格式：{Path.GetExtension(input)}（支持 JPG/PNG/WebP/BMP/TIFF）",
                    false));
            }
        }

        errors.AddRange(ValidateParameters(request));

        return errors.Count == 0
            ? ValueTask.FromResult(ToolValidationResult.Valid())
            : ValueTask.FromResult(ToolValidationResult.Invalid(errors.ToArray()));
    }

    /// <summary>Tool-specific parameter checks; base already checked the inputs.</summary>
    protected virtual IReadOnlyList<JobError> ValidateParameters(JobRequest request) =>
        Array.Empty<JobError>();

    public ValueTask<ToolEstimate> EstimateAsync(
        JobRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(new ToolEstimate(null, null));

    public virtual async Task<ToolExecutionResult> ExecuteAsync(
        JobExecutionContext context,
        CancellationToken cancellationToken)
    {
        var outputs = new List<string>();
        var failures = new List<JobError>();
        var inputs = context.Request.InputPaths;
        double done = 0;

        foreach (string input in inputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                outputs.AddRange(
                    await ProcessInputAsync(input, context, cancellationToken).ConfigureAwait(false));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                // 单项失败继续：收集错误，其余文件照常处理。
                failures.Add(new JobError(
                    "image.process.failed",
                    $"{Path.GetFileName(input)} 处理失败：{error.Message}",
                    true));
            }

            done++;
            context.Progress.Report(new JobProgress(
                done / inputs.Count * 100,
                failures.Count == 0 ? "正在处理图片" : $"正在处理图片（{failures.Count} 项失败）"));
        }

        if (outputs.Count == 0 && failures.Count > 0)
        {
            // Every input failed: surface the first error as a hard failure.
            throw new InvalidOperationException(failures[0].Message);
        }

        return failures.Count == 0
            ? ToolExecutionResult.Success(outputs)
            : ToolExecutionResult.Partial(outputs, failures[0]);
    }

    /// <summary>
    /// Default per-input flow: load → AutoOrient → <see cref="ApplyAsync"/> → save.
    /// Tools with a different shape (multi-page convert, collage canvas) override.
    /// </summary>
    protected virtual async Task<IReadOnlyList<string>> ProcessInputAsync(
        string input,
        JobExecutionContext context,
        CancellationToken cancellationToken)
    {
        using var image = new MagickImage(input);
        image.AutoOrient();

        string extension = ResolveOutputExtension(input, context.Request);
        string outputPath = ImageOutputNaming.BuildOutputPath(
            input, OutputSuffix, extension, context.Request.OutputDirectory);

        await ApplyAsync(image, context.Request, cancellationToken).ConfigureAwait(false);
        await SaveAsync(image, outputPath, context.Request, cancellationToken).ConfigureAwait(false);

        return new[] { outputPath };
    }

    /// <summary>Tool-specific pixel operations applied to the loaded image.</summary>
    protected abstract Task ApplyAsync(
        MagickImage image,
        JobRequest request,
        CancellationToken cancellationToken);

    protected string ResolveOutputExtension(string input, JobRequest request)
    {
        if (ForcedOutputFormat is not null)
        {
            return ImageFormats.ExtensionFor(ForcedOutputFormat);
        }

        if (request.Parameters.TryGetValue("format", out string? format) &&
            ImageFormats.IsSupportedOutputFormat(format))
        {
            return ImageFormats.ExtensionFor(format);
        }

        return ImageOutputNaming.SourceExtension(input);
    }

    /// <summary>
    /// Writes the image, resolving transparency when the target format cannot hold
    /// an alpha channel (frozen matrix: 透明输出到不支持格式未选背景色 → 默认白).
    /// Instance method so subclasses can override <see cref="DefaultQuality"/>.
    /// </summary>
    protected async Task SaveAsync(
        MagickImage image,
        string outputPath,
        JobRequest request,
        CancellationToken cancellationToken)
    {
        MagickFormat format = image.Format;
        string extension = Path.GetExtension(outputPath).TrimStart('.');
        if (ImageFormats.OutputFormats.TryGetValue(extension, out MagickFormat byExtension))
        {
            format = byExtension;
        }

        ImageSaveTuning.Apply(image, format, request, DefaultQuality);

        if (!ImageFormats.SupportsAlpha(format) && image.HasAlpha)
        {
            MagickColor background = ImageSaveTuning.ResolveBackground(request);
            image.BackgroundColor = background;
            image.Alpha(AlphaAction.Background);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await image.WriteAsync(outputPath, format, cancellationToken).ConfigureAwait(false);
    }

    // ---- parameter helpers -------------------------------------------------

    protected static bool TryGetInt(
        IReadOnlyDictionary<string, string> parameters, string key, out int value)
    {
        value = 0;
        return parameters.TryGetValue(key, out string? text) &&
               int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    protected static bool TryGetDouble(
        IReadOnlyDictionary<string, string> parameters, string key, out double value)
    {
        value = 0;
        return parameters.TryGetValue(key, out string? text) &&
               double.TryParse(
                   text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    protected static bool TryGetBool(
        IReadOnlyDictionary<string, string> parameters, string key, out bool value)
    {
        value = false;
        if (!parameters.TryGetValue(key, out string? text) || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (bool.TryParse(text, out value))
        {
            return true;
        }

        if (text == "1")
        {
            value = true;
            return true;
        }

        if (text == "0")
        {
            value = false;
            return true;
        }

        return false;
    }

    protected static string? TryGetString(
        IReadOnlyDictionary<string, string> parameters, string key) =>
        parameters.TryGetValue(key, out string? text) && !string.IsNullOrWhiteSpace(text)
            ? text
            : null;

    protected static int Range100(int value) => Math.Clamp(value, -100, 100);
}
