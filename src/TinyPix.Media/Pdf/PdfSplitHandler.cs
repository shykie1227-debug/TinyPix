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
/// pdf.split — FEATURE-MATRIX §6：默认每页一份；范围如 <c>1-3,5</c>；页号升序去重。
/// 输出 <c>{原名}_页_001.pdf</c> 序列。源 PDF 只读。
/// </summary>
public sealed class PdfSplitHandler : PdfToolHandlerBase
{
    public override string ToolId => "pdf.split";

    protected override void ValidateParameters(JobRequest request, List<JobError> errors)
    {
        if (request.Parameters.TryGetValue("pages", out string? spec) && !string.IsNullOrWhiteSpace(spec))
        {
            // Parse-time sanity: will be re-validated against page count at execute.
            if (!spec!.Contains('-') && !spec.Contains(',') && !int.TryParse(spec, out _))
            {
                errors.Add(new JobError("pdf.split.pages-invalid", $"无法解析页码范围：{spec}", false));
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

        IReadOnlyList<int>? pages;
        string? rangeError;
        using (PdfSharp.Pdf.PdfDocument probe = PdfSharp.Pdf.IO.PdfReader.Open(input, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))
        {
            pages = PdfPageRanges.Parse(
                context.Request.Parameters.TryGetValue("pages", out string? spec) ? spec : null,
                probe.PageCount,
                out rangeError);
        }

        if (pages is null)
        {
            throw new InvalidOperationException(rangeError ?? "页码范围无效。");
        }

        var outputs = new List<string>();
        var failures = new List<JobError>();

        for (int i = 0; i < pages.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int pageNumber = pages[i];
            try
            {
                using PdfSharp.Pdf.PdfDocument source = PdfSharp.Pdf.IO.PdfReader.Open(input, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
                using var single = new PdfSharp.Pdf.PdfDocument();
                single.AddPage(source.Pages[pageNumber - 1]);

                string output = PdfNaming.SequencePath(input, "_页", "pdf", i + 1, outputDirectory);
                single.Save(output);
                outputs.Add(output);
            }
            catch (Exception error)
            {
                failures.Add(new JobError("pdf.split.page-failed", $"页 {pageNumber} 拆分失败：{error.Message}", true));
            }

            context.Progress.Report(new JobProgress(
                (i + 1) * 100.0 / pages.Count,
                $"拆分中 {i + 1}/{pages.Count}"));
        }

        if (failures.Count > 0 && outputs.Count == 0)
        {
            throw new InvalidOperationException($"所有页面拆分失败：{failures[0].Message}");
        }

        context.Progress.Report(new JobProgress(100, $"拆分完成 · {outputs.Count} 个文件"));
        return failures.Count > 0
            ? ToolExecutionResult.Partial(outputs, failures[0])
            : ToolExecutionResult.Success(outputs);
    }
}
