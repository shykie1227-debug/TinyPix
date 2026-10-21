using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TinyPix.Core.Jobs;
using TinyPix.Core.Tools;

namespace TinyPix.Media.Tools;

/// <summary>
/// 批量重命名副本 (file.rename). Frozen defaults (FEATURE-MATRIX §8): prefix
/// <c>已整理_</c>, numbering off, find/replace empty, extension locked; when
/// numbering is on it starts at 1 with 3 digits. The tool copies content and
/// last-write time into the user-confirmed output directory - it never moves,
/// renames or touches the source (no move/rename API is used anywhere).
/// Conflicts are pre-resolved with the frozen <c> (n)</c> rule and the batch is
/// ordered by stable file name so numbering is deterministic.
/// </summary>
public sealed class FileRenameHandler : IToolHandler
{
    public string ToolId => "file.rename";

    private static readonly string[] ReservedNames =
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public ValueTask<ToolValidationResult> ValidateAsync(
        JobRequest request,
        CancellationToken cancellationToken)
    {
        var errors = new List<JobError>();

        if (request.InputPaths.Count == 0)
        {
            errors.Add(new JobError("file.rename.input.empty", "请至少添加一个文件。", false));
        }

        foreach (string input in request.InputPaths)
        {
            if (Directory.Exists(input))
            {
                errors.Add(new JobError(
                    "file.rename.input.folder", $"不支持文件夹输入：{input}", false));
                continue;
            }

            if (!File.Exists(input))
            {
                errors.Add(new JobError("file.rename.input.missing", $"文件不存在：{input}", false));
            }
        }

        if (request.InputPaths.Count > 0)
        {
            // Dry-run the first name to surface naming errors before the queue starts.
            string probe = ComposeName(
                FirstFileName(request), request, 0, out string? namingError);
            if (namingError is not null)
            {
                errors.Add(new JobError("file.rename.name-invalid", namingError, false));
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
        // 按稳定文件名排序 (frozen rule) so numbering never depends on add order.
        var inputs = context.Request.InputPaths.OrderBy(path => path, StringComparer.Ordinal).ToList();
        bool numbering = TryBool(context.Request.Parameters, "numbering");
        int start = TryInt(context.Request.Parameters, "startNumber", 1);

        Directory.CreateDirectory(context.Request.OutputDirectory);

        var outputs = new List<string>();
        var failures = new List<JobError>();
        long copiedBytes = 0;

        for (int index = 0; index < inputs.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string input = inputs[index];
            try
            {
                string name = ComposeName(
                    Path.GetFileName(input), context.Request, start + index, out string? namingError);
                if (namingError is not null)
                {
                    throw new InvalidOperationException(namingError);
                }

                string target = ResolveConflict(Path.Combine(context.Request.OutputDirectory, name));

                // File.Copy semantics, but with last-write time preserved and the
                // partial copy removed on failure (frozen: 失败删除未完成副本).
                await CopyAsync(input, target, cancellationToken).ConfigureAwait(false);
                File.SetLastWriteTimeUtc(target, new FileInfo(input).LastWriteTimeUtc);

                outputs.Add(target);
                copiedBytes += new FileInfo(target).Length;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                failures.Add(new JobError(
                    "file.rename.copy-failed",
                    $"{Path.GetFileName(input)} 复制失败：{error.Message}",
                    true));
            }

            context.Progress.Report(new JobProgress(
                (index + 1) / (double)inputs.Count * 100,
                failures.Count == 0 ? "正在复制重命名副本" : $"正在复制（{failures.Count} 项失败）"));
        }

        if (outputs.Count == 0 && failures.Count > 0)
        {
            throw new InvalidOperationException(failures[0].Message);
        }

        var result = failures.Count == 0
            ? ToolExecutionResult.Success(outputs)
            : ToolExecutionResult.Partial(outputs, failures[0]);
        return result;
    }

    private static async Task CopyAsync(
        string source, string target, CancellationToken cancellationToken)
    {
        try
        {
            await using var input = new FileStream(
                source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, useAsync: true);
            await using var output = new FileStream(
                target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true);
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (File.Exists(target))
            {
                try
                {
                    File.Delete(target);
                }
                catch
                {
                    // Best effort cleanup of the unfinished copy.
                }
            }

            throw;
        }
    }

    private static string ComposeName(
        string sourceFileName,
        JobRequest request,
        int number,
        out string? error)
    {
        error = null;
        string prefix = Param(request, "prefix") ?? "已整理_";
        bool numbering = TryBool(request.Parameters, "numbering");
        bool lockExtension = !TryBool(request.Parameters, "lockExtension", out bool unlocked) || unlocked;
        string find = Param(request, "find") ?? string.Empty;
        string replace = Param(request, "replace") ?? string.Empty;

        string extension = Path.GetExtension(sourceFileName);
        string stem = Path.GetFileNameWithoutExtension(sourceFileName);

        if (lockExtension)
        {
            // Find/replace only rewrites the stem; the extension is frozen.
            if (!string.IsNullOrEmpty(find))
            {
                stem = stem.Replace(find, replace, StringComparison.Ordinal);
            }
        }
        else if (!string.IsNullOrEmpty(find))
        {
            string whole = sourceFileName.Replace(find, replace, StringComparison.Ordinal);
            stem = Path.GetFileNameWithoutExtension(whole);
            extension = Path.GetExtension(whole);
        }

        string name = prefix + stem;
        if (numbering)
        {
            name += "_" + number.ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
        }

        name += extension;

        error = ValidateFileName(name);
        return name;
    }

    private static string ValidateFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "生成的文件名为空。";
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return $"生成的文件名包含非法字符：{name}";
        }

        string stem = Path.GetFileNameWithoutExtension(name).Trim();
        if (stem.EndsWith('.') || stem.EndsWith(' '))
        {
            return $"生成的文件名以点或空格结尾：{name}";
        }

        if (ReservedNames.Contains(stem.ToUpperInvariant()))
        {
            return $"生成的文件名是 Windows 保留名：{name}";
        }

        return null!;
    }

    /// <summary>Frozen conflict rule: append “ (n)” before the extension, from 1.</summary>
    private static string ResolveConflict(string target)
    {
        if (!File.Exists(target))
        {
            return target;
        }

        string directory = Path.GetDirectoryName(target)!;
        string extension = Path.GetExtension(target);
        string stem = Path.GetFileNameWithoutExtension(target);

        for (int n = 1; n < int.MaxValue; n++)
        {
            string candidate = Path.Combine(directory, $"{stem} ({n}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return target;
    }

    private static string FirstFileName(JobRequest request) =>
        request.InputPaths.Count > 0 ? Path.GetFileName(request.InputPaths[0]) : string.Empty;

    private static string? Param(JobRequest request, string key) =>
        request.Parameters.TryGetValue(key, out string? text) && !string.IsNullOrWhiteSpace(text)
            ? text
            : null;

    private static bool TryBool(IReadOnlyDictionary<string, string> parameters, string key, out bool value)
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

    private static bool TryBool(IReadOnlyDictionary<string, string> parameters, string key) =>
        TryBool(parameters, key, out bool value) && value;

    private static int TryInt(IReadOnlyDictionary<string, string> parameters, string key, int fallback) =>
        parameters.TryGetValue(key, out string? text) &&
        int.TryParse(text, out int parsed)
            ? parsed
            : fallback;
}
