using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TinyPix.Core.Jobs;
using TinyPix.Core.Storage;
using TinyPix.Core.Tools;
using TinyPix.Media.Video;

namespace TinyPix.App.Services;

/// <summary>
/// Shell-side entry point for running a tool job: resolves the registered handler,
/// runs the output preflight (writability / disk space / source-overwrite), then
/// enqueues through JobQueueService. Keeps the queue handler-agnostic.
/// </summary>
public sealed class JobSubmissionService
{
    private readonly JobQueueService _queue;
    private readonly ToolHandlerRegistry _registry;
    private readonly OutputPreflightService _preflight;

    public JobSubmissionService(
        JobQueueService queue,
        ToolHandlerRegistry registry,
        OutputPreflightService preflight)
    {
        _queue = queue;
        _registry = registry;
        _preflight = preflight;
    }

    public async Task<JobRecord> SubmitAsync(
        JobRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_registry.TryResolve(request.ToolId, out IToolHandler? handler) || handler is null)
        {
            throw new InvalidOperationException($"未注册的工具处理器：{request.ToolId}");
        }

        IReadOnlyList<string> candidateOutputs = CandidateOutputs(handler, request);
        var preflightRequest = new OutputPreflightRequest(
            request.InputPaths, candidateOutputs, request.OutputDirectory, EstimatedOutputBytes: 0);
        OutputPreflightResult check = _preflight.Evaluate(preflightRequest);
        if (!check.CanExecute)
        {
            JobError first = check.Issues.First(issue => issue.IsBlocking);
            JobRecord blocked = JobRecord.CreateQueued(request);
            blocked.Fail(first);
            return blocked;
        }

        return await _queue.RunAsync(request, handler, cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<string> CandidateOutputs(IToolHandler handler, JobRequest request)
    {
        if (handler is VideoOutputHandlerBase videoHandler)
        {
            return request.InputPaths
                .Select(input => videoHandler.PreviewOutputPath(input, request.OutputDirectory, request.Parameters))
                .ToList();
        }

        if (handler is VideoEditHandlerBase editHandler)
        {
            return editHandler.PreviewOutputPaths(request);
        }

        return request.InputPaths
            .Select(input => Path.Combine(
                request.OutputDirectory,
                Path.GetFileNameWithoutExtension(input) + Path.GetExtension(input)))
            .ToList();
    }
}
