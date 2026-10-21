using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TinyPix.Core.Jobs;
using TinyPix.Core.Tools;
using TinyPix.Media.Codes;

namespace TinyPix.Media.Tools;

/// <summary>
/// 二维码与条码生成 (code.generate). Frozen defaults (FEATURE-MATRIX §7):
/// QR 512×512 px with 纠错 M, margin 4 modules, PNG (SVG opt-in, QR only),
/// black on white; 1-D barcodes (Code 128 / Code 39 / EAN-13 / EAN-8 / UPC-A)
/// are 256 px tall with a 10-module quiet zone. Input is either manual text
/// (<c>content</c>) or a UTF-8 TXT file with one code per line (max 1000).
/// Output naming is frozen: <c>二维码_YYYYMMDD_HHMMSS.png</c> /
/// <c>条码_YYYYMMDD_HHMMSS.png</c>, batch runs append <c>_001</c>, then the
/// ordinary <c> (n)</c> collision rule. Generation is fully offline (E9
/// ZXing.Net for the raster, E3 Magick.NET for the PNG bytes).
/// </summary>
public sealed class CodeGenerateHandler : IToolHandler
{
    public string ToolId => "code.generate";

    private const int MaxBatchItems = 1000; // frozen: 每行一条最多 1000 条
    private const int MinSize = 64;
    private const int MaxSize = 4096;

    public ValueTask<ToolValidationResult> ValidateAsync(
        JobRequest request,
        CancellationToken cancellationToken)
    {
        var errors = new List<JobError>();

        CodeFormat? format = CodeToolCore.ParseFormat(Param(request, "format"));
        if (format is null)
        {
            errors.Add(new JobError(
                "code.generate.format-unsupported",
                "不支持的码制。支持：QR、Code 128、Code 39、EAN-13、EAN-8、UPC-A。",
                false));
            return Invalid(errors);
        }

        if (!CodeToolCore.IsValidErrorCorrection(Param(request, "ecLevel")))
        {
            errors.Add(new JobError(
                "code.generate.ec-unsupported", "纠错级别必须是 L、M、Q 或 H。", false));
        }

        int size = TryInt(request.Parameters, "size", CodeToolCore.DefaultQrSize);
        if (size < MinSize || size > MaxSize)
        {
            errors.Add(new JobError(
                "code.generate.size-out-of-range",
                $"二维码边长必须在 {MinSize}–{MaxSize} px 之间（默认 {CodeToolCore.DefaultQrSize}）。",
                false));
        }

        if (!TryOutput(request.Parameters, out CodeOutputFormat output, out string? outputError))
        {
            errors.Add(new JobError("code.generate.output-unsupported", outputError!, false));
        }
        else if (output == CodeOutputFormat.Svg && format != CodeFormat.Qr)
        {
            errors.Add(new JobError(
                "code.generate.svg-qr-only", "SVG 输出只支持二维码；条码请使用 PNG。", false));
        }

        string? content = Param(request, "content");
        bool hasManual = !string.IsNullOrWhiteSpace(content);
        var txtInputs = request.InputPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToList();

        if (hasManual && txtInputs.Count > 0)
        {
            errors.Add(new JobError(
                "code.generate.ambiguous-input",
                "手动文本和 TXT 文件不能同时提供：请只输入内容，或只选择 TXT 文件。",
                false));
            return Invalid(errors);
        }

        if (!hasManual && txtInputs.Count == 0)
        {
            errors.Add(new JobError(
                "code.generate.input.empty", "请输入生成内容，或选择一个 UTF-8 编码的 TXT 文件。", false));
            return Invalid(errors);
        }

        foreach (string input in txtInputs)
        {
            if (Directory.Exists(input))
            {
                errors.Add(new JobError(
                    "code.generate.input.folder", $"不支持文件夹输入：{input}", false));
                continue;
            }

            if (!File.Exists(input))
            {
                errors.Add(new JobError("code.generate.input.missing", $"文件不存在：{input}", false));
                continue;
            }

            if (!Path.GetExtension(input).Equals(".txt", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new JobError(
                    "code.generate.input.not-txt", $"批量生成只支持 .txt 文件：{Path.GetFileName(input)}", false));
                continue;
            }

            List<string> lines = ReadLines(input, out string? readError);
            if (readError is not null)
            {
                errors.Add(new JobError("code.generate.txt-unreadable", $"{Path.GetFileName(input)}：{readError}", false));
                continue;
            }

            if (lines.Count == 0)
            {
                errors.Add(new JobError(
                    "code.generate.txt-empty", $"TXT 文件没有任何有效内容行：{Path.GetFileName(input)}", false));
                continue;
            }

            if (lines.Count > MaxBatchItems)
            {
                errors.Add(new JobError(
                    "code.generate.too-many",
                    $"TXT 文件超过 {MaxBatchItems} 条上限（当前 {lines.Count} 条）。",
                    false));
                continue;
            }

            foreach (string line in lines)
            {
                if (CodeToolCore.ValidateContent(format.Value, line) is string contentError)
                {
                    errors.Add(new JobError(
                        "code.generate.content-invalid",
                        $"{Path.GetFileName(input)}：{contentError}",
                        false));
                    break; // one clear error per file is enough
                }
            }
        }

        if (hasManual && CodeToolCore.ValidateContent(format.Value, content!) is string manualError)
        {
            errors.Add(new JobError("code.generate.content-invalid", manualError, false));
        }

        return Invalid(errors);
    }

    public ValueTask<ToolEstimate> EstimateAsync(
        JobRequest request,
        CancellationToken cancellationToken)
    {
        // ZXing + a small Magick.NET encode: milliseconds per code.
        int items = ManualItems(request).Count + TxtItems(request).Count;
        return ValueTask.FromResult(new ToolEstimate(
            TimeSpan.FromMilliseconds(Math.Max(50, items * 40)),
            items * 24 * 1024));
    }

    public async Task<ToolExecutionResult> ExecuteAsync(
        JobExecutionContext context,
        CancellationToken cancellationToken)
    {
        CodeFormat format = CodeToolCore.ParseFormat(Param(context.Request, "format"))!.Value;
        bool svg = TryOutput(context.Request.Parameters, out CodeOutputFormat output, out _) &&
                   output == CodeOutputFormat.Svg;
        int size = TryInt(context.Request.Parameters, "size", CodeToolCore.DefaultQrSize);
        var options = new CodeRenderOptions(size, Param(context.Request, "ecLevel"), svg ? CodeOutputFormat.Svg : CodeOutputFormat.Png);

        List<(string Content, string Label)> items = ManualItems(context.Request).Count > 0
            ? ManualItems(context.Request).Select(line => (line, "手动输入")).ToList()
            : TxtItems(context.Request)
                .Select(line => (line, Truncate(line, 24)))
                .ToList();

        string stamp = CodeToolCore.Stamp();
        Directory.CreateDirectory(context.Request.OutputDirectory);

        var outputs = new List<string>();
        var failures = new List<JobError>();

        for (int index = 0; index < items.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            (string content, string label) = items[index];
            try
            {
                RenderedCode rendered = CodeToolCore.Render(format, content, options);
                string path = CodeToolCore.BuildGeneratedPath(
                    context.Request.OutputDirectory,
                    format,
                    rendered.Format,
                    stamp,
                    items.Count > 1 ? index + 1 : 0);

                if (rendered.Format == CodeOutputFormat.Svg)
                {
                    await CodeToolCore.WriteAtomicTextAsync(path, rendered.Svg, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await CodeToolCore.WriteAtomicAsync(path, rendered.Png, cancellationToken).ConfigureAwait(false);
                }

                outputs.Add(path);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                failures.Add(new JobError(
                    MapFailure(error),
                    $"{label} 生成失败：{error.Message}",
                    true));
            }

            context.Progress.Report(new JobProgress(
                (index + 1) / (double)items.Count * 100,
                failures.Count == 0 ? $"正在生成 {index + 1}/{items.Count}" : $"正在生成（{failures.Count} 项失败）"));
        }

        if (outputs.Count == 0 && failures.Count > 0)
        {
            throw new InvalidOperationException(failures[0].Message);
        }

        return failures.Count == 0
            ? ToolExecutionResult.Success(outputs)
            : ToolExecutionResult.Partial(outputs, failures[0]);
    }

    /// <summary>
    /// ZXing throws a mix of ArgumentException / WriterException for capacity and
    /// checksum problems; map them onto the frozen error vocabulary.
    /// </summary>
    private static string MapFailure(Exception error) => error switch
    {
        ArgumentException argument when argument.Message.Contains("checksum", StringComparison.OrdinalIgnoreCase) =>
            "code.generate.checksum-invalid",
        ArgumentException => "code.generate.content-invalid",
        _ when error.GetType().Name == "WriterException" =>
            "code.generate.capacity-exceeded",
        _ => "code.generate.render-failed",
    };

    private static List<string> ManualItems(JobRequest request)
    {
        string? content = Param(request, "content");
        if (string.IsNullOrWhiteSpace(content))
        {
            return new List<string>();
        }

        // Manual text is ONE code (QR byte mode keeps newlines verbatim); a
        // trailing newline from pasting is never intentional.
        return new List<string> { content.TrimEnd() };
    }

    private static List<string> TxtItems(JobRequest request)
    {
        var lines = new List<string>();
        foreach (string input in request.InputPaths)
        {
            if (!Path.GetExtension(input).Equals(".txt", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            lines.AddRange(ReadLines(input, out _));
        }

        return lines;
    }

    /// <summary>
    /// Reads a UTF-8 TXT file (frozen input format). Rejects UTF-16 and invalid
    /// UTF-8 with a precise message instead of silently producing mojibake codes.
    /// Blank lines are skipped - they are not codes.
    /// </summary>
    private static List<string> ReadLines(string path, out string? error)
    {
        error = null;
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 2 &&
                ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)))
            {
                error = "检测到 UTF-16 编码，请另存为 UTF-8 编码的 TXT 文件。";
                return new List<string>();
            }

            string text;
            try
            {
                text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                error = "不是有效的 UTF-8 文件，请检查编码后重试。";
                return new List<string>();
            }

            return text
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = exception.Message;
            return new List<string>();
        }
    }

    private static bool TryOutput(IReadOnlyDictionary<string, string> parameters, out CodeOutputFormat format, out string? error)
    {
        format = CodeOutputFormat.Png;
        error = null;
        if (!parameters.TryGetValue("output", out string? text) || string.IsNullOrWhiteSpace(text))
        {
            return true; // frozen default: PNG
        }

        switch (text.Trim().ToLowerInvariant())
        {
            case "png":
                return true;
            case "svg":
                format = CodeOutputFormat.Svg;
                return true;
            default:
                error = "输出格式必须是 png 或 svg（SVG 仅二维码支持）。";
                return false;
        }
    }

    private static ValueTask<ToolValidationResult> Invalid(List<JobError> errors) =>
        ValueTask.FromResult(ToolValidationResult.Invalid(errors.ToArray()));

    private static string? Param(JobRequest request, string key) =>
        request.Parameters.TryGetValue(key, out string? text) && !string.IsNullOrWhiteSpace(text)
            ? text
            : null;

    private static int TryInt(IReadOnlyDictionary<string, string> parameters, string key, int fallback) =>
        parameters.TryGetValue(key, out string? text) &&
        int.TryParse(text, out int parsed)
            ? parsed
            : fallback;

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text.Substring(0, max) + "…";
}
