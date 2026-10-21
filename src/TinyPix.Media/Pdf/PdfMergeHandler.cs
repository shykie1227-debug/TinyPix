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
/// pdf.merge — FEATURE-MATRIX §6：按列表/页序合并多个 PDF；保留页面尺寸；
/// 保留可由 PDFsharp 安全导入的书签，无法导入则丢弃；不插空白页。
/// 输出 <c>{首个原名}_合并.pdf</c>。
/// </summary>
public sealed class PdfMergeHandler : PdfToolHandlerBase
{
    public override string ToolId => "pdf.merge";

    protected override int MinimumInputs => 2;

    public override async Task<ToolExecutionResult> ExecuteAsync(
        JobExecutionContext context,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        PdfRules.EnsureInitialized();

        var inputs = context.Request.InputPaths.Where(File.Exists).ToList();
        if (inputs.Count < 2)
        {
            throw new InvalidOperationException("合并至少需要 2 个 PDF。");
        }

        string outputDirectory = context.Request.OutputDirectory;
        Directory.CreateDirectory(outputDirectory);
        string output = PdfNaming.Build(inputs[0], "_合并", outputDirectory);

        int totalPages = 0;
        int bookmarkFailures = 0;

        using (var target = new PdfSharp.Pdf.PdfDocument())
        {
            for (int inputIndex = 0; inputIndex < inputs.Count; inputIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using PdfSharp.Pdf.PdfDocument source = PdfSharp.Pdf.IO.PdfReader.Open(
                    inputs[inputIndex], PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);

                for (int i = 0; i < source.PageCount; i++)
                {
                    target.AddPage(source.Pages[i]);
                    totalPages++;
                }

                // Bookmarks: attempt to copy outline entries whose destinations
                // are page references inside this source. Any outline that
                // fails to resolve (external link, named destination) is
                // silently dropped per the frozen matrix.
                try
                {
                    if (source.Outlines is not null)
                    {
                        foreach (PdfSharp.Pdf.PdfOutline outline in source.Outlines)
                        {
                            try
                            {
                                if (outline.DestinationPage is not null)
                                {
                                    var copy = target.Outlines.Add(outline.Title, outline.DestinationPage);
                                    _ = copy;
                                }
                                else
                                {
                                    bookmarkFailures++;
                                }
                            }
                            catch
                            {
                                bookmarkFailures++;
                            }
                        }
                    }
                }
                catch
                {
                    bookmarkFailures++;
                }

                context.Progress.Report(new JobProgress(
                    (inputIndex + 1) * 100.0 / inputs.Count,
                    $"已合并 {inputIndex + 1}/{inputs.Count} · 累计 {totalPages} 页"));
            }

            target.Save(output);
        }

        context.Progress.Report(new JobProgress(100, $"合并完成 · {inputs.Count} 个文件 · {totalPages} 页"));
        return ToolExecutionResult.Success(new[] { output });
    }
}
