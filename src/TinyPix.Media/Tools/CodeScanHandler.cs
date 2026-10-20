using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TinyPix.Core.Jobs;
using TinyPix.Core.Tools;
using TinyPix.Media.Codes;
using TinyPix.Media.Images;

namespace TinyPix.Media.Tools;

/// <summary>
/// 二维码与条码识别 (code.scan). Frozen defaults (FEATURE-MATRIX §7): ZXing.Net
/// (E9) over a Magick.NET (E3) decode; AutoRotate=true realised as the four
/// raster rotations 0/90/180/270; TryHarder=true; at most 50 codes per image.
/// Input images are JPG/JPEG/PNG/WebP/BMP (clipboard images are the shell's
/// E11 job). Recognised URLs are only ever displayed/copied by the UI - this
/// handler never opens anything. Optional export writes
/// <c>{原名}_码识别.txt</c>.
/// </summary>
public sealed class CodeScanHandler : IToolHandler
{
    public string ToolId => "code.scan";

    /// <summary>
    /// Frozen scan input set (matrix §7). TIFF deliberately excluded: the frozen
    /// list is JPG/JPEG/PNG/WebP/BMP + clipboard.
    /// </summary>
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".bmp",
    };

    public ValueTask<ToolValidationResult> ValidateAsync(
        JobRequest request,
        CancellationToken cancellationToken)
    {
        var errors = new List<JobError>();

        if (request.InputPaths.Count == 0)
        {
            errors.Add(new JobError("code.scan.input.empty", "请至少添加一张图片。", false));
        }

        if (CodeToolCore.ParseFormat(Param(request, "format")) is null &&
            !string.Equals(Param(request, "format"), "AUTO", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(Param(request, "format")))
        {
            errors.Add(new JobError(
                "code.scan.format-unsupported",
                "不支持的码制。支持：AUTO、QR、Code 128、Code 39、EAN-13、EAN-8、UPC-A。",
                false));
        }

        foreach (string input in request.InputPaths)
        {
            if (Directory.Exists(input))
            {
                errors.Add(new JobError(
                    "code.scan.input.folder", $"不支持文件夹输入：{input}", false));
                continue;
            }

            if (!File.Exists(input))
            {
                errors.Add(new JobError("code.scan.input.missing", $"文件不存在：{input}", false));
                continue;
            }

            if (!SupportedExtensions.Contains(Path.GetExtension(input)))
            {
                errors.Add(new JobError(
                    "code.scan.input.unsupported",
                    $"不支持的图片格式：{Path.GetFileName(input)}（支持 JPG/JPEG/PNG/WebP/BMP）。",
                    false));
            }
        }

        return errors.Count == 0
            ? ValueTask.FromResult(ToolValidationResult.Valid())
            : ValueTask.FromResult(ToolValidationResult.Invalid(errors.ToArray()));
    }

    public ValueTask<ToolEstimate> EstimateAsync(
        JobRequest request,
        CancellationToken cancellationToken)
    {
        long total = 0;
        foreach (string input in request.InputPaths)
        {
            if (File.Exists(input))
            {
                total += new FileInfo(input).Length;
            }
        }

        // Four rotations × TryHarder dominates; assume ~0.5 s per MP of raster.
        return ValueTask.FromResult(new ToolEstimate(TimeSpan.FromSeconds(2), total));
    }

    public async Task<ToolExecutionResult> ExecuteAsync(
        JobExecutionContext context,
        CancellationToken cancellationToken)
    {
        bool export = TryBool(context.Request.Parameters, "export");
        var outputs = new List<string>();
        var failures = new List<JobError>();

        double done = 0;
        foreach (string input in context.Request.InputPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                ScanOutcome outcome = await Task.Run(
                    () => ScanOne(input, ResolveFilter(context.Request)), cancellationToken)
                    .ConfigureAwait(false);

                if (export)
                {
                    string report = ImageOutputNaming.BuildOutputPath(
                        input, "_码识别", "txt", context.Request.OutputDirectory);
                    await CodeToolCore.WriteAtomicTextAsync(
                        report, BuildReport(input, outcome), cancellationToken).ConfigureAwait(false);
                    outputs.Add(report);
                }

                if (outcome.Codes.Count == 0)
                {
                    failures.Add(new JobError(
                        "code.scan.none-detected",
                        $"{Path.GetFileName(input)}：未检测到二维码或条码（可能模糊、过小或超出支持码制）。",
                        false));
                }
                else if (outcome.Truncated)
                {
                    failures.Add(new JobError(
                        "code.scan.truncated",
                        $"{Path.GetFileName(input)}：检测到超过 {CodeToolCore.MaxResults} 个码，已按前 {CodeToolCore.MaxResults} 个截断。",
                        true));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                failures.Add(new JobError(
                    "code.scan.read-failed",
                    $"{Path.GetFileName(input)} 读取失败：{error.Message}",
                    true));
            }

            done++;
            context.Progress.Report(new JobProgress(
                done / context.Request.InputPaths.Count * 100,
                $"正在识别 {Path.GetFileName(input)}"));
        }

        if (outputs.Count == 0 && failures.Count > 0 &&
            failures.All(failure => failure.Code == "code.scan.none-detected"))
        {
            throw new InvalidOperationException(failures[0].Message);
        }

        return failures.Count == 0
            ? ToolExecutionResult.Success(outputs)
            : ToolExecutionResult.Partial(outputs, failures[0]);
    }

    /// <summary>
    /// Read-only scan used by the UI preview and the smoke harness: decodes one
    /// image and returns every code found. Never writes anything.
    /// </summary>
    public static ScanOutcome ScanOne(string path, CodeFormat? filter)
    {
        (byte[] pixels, int width, int height) = CodeToolCore.LoadForScan(path);
        IReadOnlyList<DecodedCode> codes = CodeToolCore.Decode(pixels, width, height, filter);
        return new ScanOutcome(codes, codes.Count >= CodeToolCore.MaxResults);
    }

    private static CodeFormat? ResolveFilter(JobRequest request)
    {
        string? text = Param(request, "format");
        if (string.IsNullOrWhiteSpace(text) ||
            text.Equals("AUTO", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return CodeToolCore.ParseFormat(text);
    }

    private static string BuildReport(string path, ScanOutcome outcome)
    {
        var builder = new StringBuilder();
        builder.AppendLine("TinyPix 二维码与条码识别");
        builder.AppendLine($"生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"文件：{path}");
        builder.AppendLine($"识别到 {outcome.Codes.Count} 个码"
            + (outcome.Truncated ? $"（超过 {CodeToolCore.MaxResults} 个，已截断）" : string.Empty));
        builder.AppendLine();
        builder.AppendLine("码制\t内容");
        foreach (DecodedCode code in outcome.Codes)
        {
            builder.AppendLine($"{code.DisplayFormat}\t{code.Text}");
        }

        return builder.ToString();
    }

    private static string? Param(JobRequest request, string key) =>
        request.Parameters.TryGetValue(key, out string? text) && !string.IsNullOrWhiteSpace(text)
            ? text
            : null;

    private static bool TryBool(IReadOnlyDictionary<string, string> parameters, string key) =>
        parameters.TryGetValue(key, out string? text) &&
        (text == "1" || (bool.TryParse(text, out bool parsed) && parsed));
}

public sealed record ScanOutcome(IReadOnlyList<DecodedCode> Codes, bool Truncated);
