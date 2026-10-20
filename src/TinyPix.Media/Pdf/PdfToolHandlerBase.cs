using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TinyPix.Core.Jobs;
using TinyPix.Core.Tools;

namespace TinyPix.Media.Pdf;

/// <summary>
/// Shared plumbing for the Phase 5 pdf.* handlers: input existence / PDF
/// readability checks and the FEATURE-MATRIX §2.2 modify-gate (signed PDF is a
/// hard block before queueing). Subclasses decide which gate applies.
/// </summary>
public abstract class PdfToolHandlerBase : IToolHandler
{
    public abstract string ToolId { get; }

    /// <summary>Modify-type tools block signed + encrypted PDFs; read-only tools only block encrypted/broken.</summary>
    protected virtual bool IsModifyTool => true;

    /// <summary>Minimum number of input files (merge requires ≥2).</summary>
    protected virtual int MinimumInputs => 1;

    /// <summary>Allowed input extensions (lowercase, with dot).</summary>
    protected virtual IReadOnlyCollection<string> AllowedExtensions { get; } = new[] { ".pdf" };

    public virtual async ValueTask<ToolValidationResult> ValidateAsync(
        JobRequest request,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        var errors = new List<JobError>();

        if (request.InputPaths.Count < MinimumInputs)
        {
            errors.Add(new JobError(
                $"{ToolId}.input-count",
                $"至少需要 {MinimumInputs} 个输入文件，当前 {request.InputPaths.Count} 个。",
                false));
        }

        foreach (string input in request.InputPaths)
        {
            if (!File.Exists(input))
            {
                errors.Add(new JobError($"{ToolId}.input-missing", $"文件不存在：{input}", false));
                continue;
            }

            string extension = Path.GetExtension(input).ToLowerInvariant();
            if (!AllowedExtensions.Contains(extension))
            {
                errors.Add(new JobError(
                    $"{ToolId}.input-type",
                    $"不支持的文件类型：{Path.GetFileName(input)}（支持 {string.Join("、", AllowedExtensions)}）",
                    false));
                continue;
            }

            string? gateError = IsModifyTool ? PdfRules.CheckModifyAllowed(input) : CheckReadOnlyAllowed(input);
            if (gateError is not null)
            {
                errors.Add(new JobError($"{ToolId}.input-blocked", gateError, false));
            }
        }

        ValidateParameters(request, errors);
        return new ToolValidationResult(errors);
    }

    /// <summary>Subclass hook for tool-specific parameter checks.</summary>
    protected virtual void ValidateParameters(JobRequest request, List<JobError> errors)
    {
    }

    public virtual ValueTask<ToolEstimate> EstimateAsync(
        JobRequest request,
        CancellationToken cancellationToken)
    {
        long totalBytes = 0;
        foreach (string input in request.InputPaths)
        {
            if (File.Exists(input))
            {
                totalBytes += new FileInfo(input).Length;
            }
        }

        return ValueTask.FromResult(new ToolEstimate(TimeSpan.FromSeconds(2 + totalBytes / (8 * 1024 * 1024.0)), null));
    }

    public abstract Task<ToolExecutionResult> ExecuteAsync(
        JobExecutionContext context,
        CancellationToken cancellationToken);

    /// <summary>Read-only tools (to-image, info) accept signed PDFs but reject encrypted/broken ones.</summary>
    internal static string? CheckReadOnlyAllowed(string path)
    {
        try
        {
            using UglyToad.PdfPig.PdfDocument document = UglyToad.PdfPig.PdfDocument.Open(path);
            if (document.IsEncrypted)
            {
                return "该 PDF 已加密，TinyPix 不接收密码，无法处理。";
            }
        }
        catch (Exception error)
        {
            return $"无法解析该 PDF：{error.Message}";
        }

        return null;
    }

    /// <summary>Parse a bool parameter ("1"/"true"/"yes"/"on" → true).</summary>
    protected static bool TryBool(IReadOnlyDictionary<string, string> parameters, string key)
    {
        return parameters.TryGetValue(key, out string? raw) &&
               (raw == "1" || raw.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                raw.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                raw.Equals("on", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Parse an int parameter with fallback.</summary>
    protected static int TryInt(IReadOnlyDictionary<string, string> parameters, string key, int fallback)
    {
        return parameters.TryGetValue(key, out string? raw) && int.TryParse(raw, out int value) ? value : fallback;
    }
}
