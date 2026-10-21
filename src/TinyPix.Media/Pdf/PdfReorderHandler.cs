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
/// pdf.reorder — FEATURE-MATRIX §6：页面排序与删除；输出至少 1 页；
/// 顺序用逗号分隔的 1-based 页号列表（重复=复制，省略=删除）。
/// 输出 <c>{原名}_重排.pdf</c>。
/// </summary>
public sealed class PdfReorderHandler : PdfToolHandlerBase
{
    public override string ToolId => "pdf.reorder";

    protected override void ValidateParameters(JobRequest request, List<JobError> errors)
    {
        if (!request.Parameters.TryGetValue("order", out string? order) || string.IsNullOrWhiteSpace(order))
        {
            errors.Add(new JobError("pdf.reorder.order-missing", "缺少页序参数 order（如 3,1,2,1）。", false));
            return;
        }

        string[] parts = order!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            errors.Add(new JobError("pdf.reorder.order-empty", "页序参数为空。", false));
            return;
        }

        foreach (string part in parts)
        {
            if (!int.TryParse(part, out int page) || page < 1)
            {
                errors.Add(new JobError("pdf.reorder.order-invalid", $"无效页号：{part}", false));
                return;
            }
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

        string orderSpec = context.Request.Parameters["order"];
        int[] pageNumbers = orderSpec
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(int.Parse)
            .ToArray();

        using PdfSharp.Pdf.PdfDocument source = PdfSharp.Pdf.IO.PdfReader.Open(
            input, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);

        foreach (int pageNumber in pageNumbers)
        {
            if (pageNumber > source.PageCount)
            {
                throw new InvalidOperationException($"页号 {pageNumber} 超出范围（共 {source.PageCount} 页）。");
            }
        }

        if (pageNumbers.Length == 0)
        {
            throw new InvalidOperationException("输出至少需要 1 页。");
        }

        string output = PdfNaming.Build(input, "_重排", outputDirectory);

        using (var target = new PdfSharp.Pdf.PdfDocument())
        {
            for (int i = 0; i < pageNumbers.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                target.AddPage(source.Pages[pageNumbers[i] - 1]);
                context.Progress.Report(new JobProgress(
                    (i + 1) * 100.0 / pageNumbers.Length,
                    $"重排中 {i + 1}/{pageNumbers.Length}"));
            }

            target.Save(output);
        }

        context.Progress.Report(new JobProgress(100, $"重排完成 · {source.PageCount} 页 → {pageNumbers.Length} 页"));
        return ToolExecutionResult.Success(new[] { output });
    }
}
