using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TinyPix.Core.Jobs;
using TinyPix.Core.Tools;

namespace TinyPix.Media.Tools;

/// <summary>
/// 文件校验值 (file.hash). Frozen defaults (FEATURE-MATRIX §8): SHA-256 with a
/// 1 MiB streaming buffer; MD5 is opt-in and permanently labelled
/// “仅兼容用途，不用于安全校验”. An optional expected value is compared
/// case-insensitively and ignoring whitespace. Sources are read-only: the file
/// is opened for reading without locking it for writing and is never modified.
/// </summary>
public sealed class FileHashHandler : IToolHandler
{
    public string ToolId => "file.hash";

    private const int BufferSize = 1024 * 1024; // frozen 1 MiB streaming buffer

    public ValueTask<ToolValidationResult> ValidateAsync(
        JobRequest request,
        CancellationToken cancellationToken)
    {
        var errors = new List<JobError>();

        if (request.InputPaths.Count == 0)
        {
            errors.Add(new JobError("file.hash.input.empty", "请至少添加一个文件。", false));
        }

        string algorithm = ResolveAlgorithm(request);
        if (algorithm is not ("SHA256" or "MD5"))
        {
            errors.Add(new JobError(
                "file.hash.algorithm-unsupported", $"不支持的校验算法：{algorithm}（支持 SHA-256 / MD5）", false));
        }

        string? expected = TryExpected(request);
        if (expected is not null)
        {
            int hexLength = algorithm == "MD5" ? 32 : 64;
            if (expected.Length != hexLength || !IsHex(expected))
            {
                errors.Add(new JobError(
                    "file.hash.expected-malformed",
                    $"期望值必须是 {hexLength} 位十六进制字符（{algorithm}）。",
                    false));
            }
        }

        foreach (string input in request.InputPaths)
        {
            if (Directory.Exists(input))
            {
                errors.Add(new JobError(
                    "file.hash.input.folder", $"不支持文件夹输入：{input}", false));
            }
            else if (!File.Exists(input))
            {
                errors.Add(new JobError("file.hash.input.missing", $"文件不存在：{input}", false));
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

        return ValueTask.FromResult(new ToolEstimate(null, total));
    }

    public async Task<ToolExecutionResult> ExecuteAsync(
        JobExecutionContext context,
        CancellationToken cancellationToken)
    {
        string algorithm = ResolveAlgorithm(context.Request);
        string? expected = TryExpected(context.Request);
        bool export = TryBool(context.Request.Parameters, "export");
        var lines = new List<string>();
        var outputs = new List<string>();
        var failures = new List<JobError>();

        double done = 0;
        foreach (string input in context.Request.InputPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                HashOutcome outcome = await ComputeAsync(
                    input, algorithm, expected, cancellationToken).ConfigureAwait(false);

                lines.Add(FormatLine(input, algorithm, outcome));
                context.Progress.Report(new JobProgress(
                    done / context.Request.InputPaths.Count * 100,
                    $"正在计算 {Path.GetFileName(input)}"));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                failures.Add(new JobError(
                    "file.hash.read-failed",
                    $"{Path.GetFileName(input)} 读取失败：{error.Message}",
                    true));
            }

            done++;
        }

        if (export && lines.Count > 0)
        {
            string report = Path.Combine(
                context.Request.OutputDirectory,
                $"文件校验值_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            Directory.CreateDirectory(context.Request.OutputDirectory);
            await File.WriteAllTextAsync(report, BuildReport(algorithm, lines), Encoding.UTF8, cancellationToken)
                .ConfigureAwait(false);
            outputs.Add(report);
        }

        if (outputs.Count == 0 && failures.Count > 0)
        {
            throw new InvalidOperationException(failures[0].Message);
        }

        return failures.Count == 0
            ? ToolExecutionResult.Success(outputs)
            : ToolExecutionResult.Partial(outputs, failures[0]);
    }

    private static async Task<HashOutcome> ComputeAsync(
        string input,
        string algorithm,
        string? expected,
        CancellationToken cancellationToken)
    {
        long lengthBefore = new FileInfo(input).Length;
        DateTime writeBefore = new FileInfo(input).LastWriteTimeUtc;

        using HashAlgorithm hash = algorithm == "MD5" ? MD5.Create() : SHA256.Create();
        await using var stream = new FileStream(
            input, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                hash.TransformBlock(buffer, 0, read, null, 0);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        string value = Convert.ToHexString(hash.Hash!);

        var info = new FileInfo(input);
        bool stale = info.Length != lengthBefore || info.LastWriteTimeUtc != writeBefore;

        string status = stale
            ? "已作废（计算期间文件大小或时间变化）"
            : expected is null
                ? "未比对"
                : string.Equals(value, expected, StringComparison.OrdinalIgnoreCase)
                    ? "匹配"
                    : "不匹配";

        return new HashOutcome(value, status, info.Length);
    }

    private static string FormatLine(string input, string algorithm, HashOutcome outcome) =>
        $"{Path.GetFileName(input)}\t{outcome.Length.ToString(CultureInfo.InvariantCulture)}\t{outcome.Value}\t{outcome.Status}";

    private static string BuildReport(string algorithm, IReadOnlyList<string> lines)
    {
        var builder = new StringBuilder();
        builder.AppendLine("TinyPix 文件校验值");
        builder.AppendLine($"生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"算法：{(algorithm == "MD5" ? "MD5（仅兼容用途，不用于安全校验）" : "SHA-256")}");
        builder.AppendLine();
        builder.AppendLine("文件名\t大小(字节)\t校验值\t状态");
        foreach (string line in lines)
        {
            builder.AppendLine(line);
        }

        return builder.ToString();
    }

    private static string ResolveAlgorithm(JobRequest request)
    {
        if (request.Parameters.TryGetValue("algorithm", out string? text) &&
            !string.IsNullOrWhiteSpace(text))
        {
            return text.Trim().ToUpperInvariant() switch
            {
                "SHA256" or "SHA-256" => "SHA256",
                "MD5" => "MD5",
                _ => text.Trim().ToUpperInvariant(),
            };
        }

        return "SHA256";
    }

    private static string? TryExpected(JobRequest request)
    {
        if (!request.Parameters.TryGetValue("expected", out string? text) ||
            string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // 期望值比对忽略大小写和空格 (frozen rule).
        return new string(text.Where(char.IsLetterOrDigit).ToArray())
            .Replace(" ", string.Empty)
            .ToUpperInvariant();
    }

    private static bool TryBool(IReadOnlyDictionary<string, string> parameters, string key) =>
        parameters.TryGetValue(key, out string? text) &&
        (text == "1" || bool.TryParse(text, out bool parsed) && parsed);

    private static bool IsHex(string text)
    {
        foreach (char c in text)
        {
            if (!Uri.IsHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    private sealed record HashOutcome(string Value, string Status, long Length);
}
