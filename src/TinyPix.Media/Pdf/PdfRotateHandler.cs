using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TinyPix.Core.Jobs;
using TinyPix.Core.Tools;

namespace TinyPix.Media.Pdf;

/// <summary>
/// pdf.rotate — FEATURE-MATRIX §6：默认当前页顺时针 90°；可选 180/270°；
/// 范围当前/全部/奇数/偶数/明确页码。输出 <c>{原名}_旋转.pdf</c>。
/// </summary>
public sealed class PdfRotateHandler : PdfToolHandlerBase
{
    public override string ToolId => "pdf.rotate";

    protected override void ValidateParameters(JobRequest request, List<JobError> errors)
    {
        int angle = TryInt(request.Parameters, "angle", 90);
        if (angle % 90 != 0 || angle < 90 || angle > 270)
        {
            errors.Add(new JobError("pdf.rotate.angle-invalid", $"旋转角度必须是 90/180/270，当前 {angle}°。", false));
        }

        string scope = request.Parameters.TryGetValue("scope", out string? raw) ? (raw ?? "current") : "current";
        var validScopes = new[] { "current", "all", "odd", "even", "explicit" };
        if (!validScopes.Contains(scope.Trim().ToLowerInvariant()))
        {
            errors.Add(new JobError("pdf.rotate.scope-invalid", $"未知范围：{scope}（支持 current/all/odd/even/explicit）。", false));
        }
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(
        JobExecutionContext context,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        PdfRules.EnsureInitialized();

        string input = context.Request.InputPaths[0];
        string outputDirectory = context.Request.OutputDirectory;
        Directory.CreateDirectory(outputDirectory);

        int angle = TryInt(context.Request.Parameters, "angle", 90);
        string scope = context.Request.Parameters.TryGetValue("scope", out string? raw) ? (raw ?? "current") : "current";
        int currentPage = TryInt(context.Request.Parameters, "currentPage", 1);
        string pagesSpec = context.Request.Parameters.TryGetValue("pages", out string? p) ? (p ?? "") : "";

        using PdfSharp.Pdf.PdfDocument document = PdfSharp.Pdf.IO.PdfReader.Open(
            input, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Modify);

        IReadOnlyList<int> explicitPages = PdfPageRanges.Parse(
            pagesSpec, document.PageCount, out _) ?? Array.Empty<int>();

        IReadOnlyList<int> targetPages = PdfPageRanges.Select(
            scope, explicitPages, currentPage, document.PageCount, out string? selectError);

        if (selectError is not null)
        {
            throw new InvalidOperationException(selectError);
        }

        var targetList = targetPages.ToList();
        foreach (int pageNumber in targetList)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PdfSharp.Pdf.PdfPage page = document.Pages[pageNumber - 1];
            page.Rotate = (page.Rotate + angle) % 360;
            context.Progress.Report(new JobProgress(
                targetList.IndexOf(pageNumber) * 100.0 / targetList.Count,
                $"旋转页 {pageNumber}/{document.PageCount}"));
        }

        string output = PdfNaming.Build(input, "_旋转", outputDirectory);
        document.Save(output);

        context.Progress.Report(new JobProgress(100, $"旋转完成 · {targetPages.Count} 页 × {angle}°"));
        return ToolExecutionResult.Success(new[] { output });
    }
}
