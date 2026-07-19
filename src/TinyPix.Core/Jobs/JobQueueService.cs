using System.Collections.Concurrent;
using TinyPix.Core.Tools;

namespace TinyPix.Core.Jobs;

public sealed class JobQueueService : IAsyncDisposable
{
    private readonly SemaphoreSlim _heavySlots = new(1, 1);
    private readonly SemaphoreSlim _lightSlots = new(2, 2);
    private readonly ConcurrentDictionary<Guid, RunningJob> _jobs = new();
    private readonly object _lifecycleGate = new();
    private bool _disposed;
    private Task? _disposeTask;

    public async Task<JobRecord> RunAsync(
        JobRequest request,
        IToolHandler handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(handler);
        if (!string.Equals(request.ToolId, handler.ToolId, StringComparison.Ordinal))
        {
            throw new ArgumentException("The handler tool id does not match the request.", nameof(handler));
        }

        JobRecord record = JobRecord.CreateQueued(request);
        var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var running = new RunningJob(
            record,
            linkedCancellation,
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        lock (_lifecycleGate)
        {
            if (_disposed)
            {
                linkedCancellation.Dispose();
                throw new ObjectDisposedException(nameof(JobQueueService));
            }

            if (!_jobs.TryAdd(request.Id, running))
            {
                linkedCancellation.Dispose();
                throw new InvalidOperationException($"Job {request.Id} is already registered.");
            }
        }

        SemaphoreSlim slots = request.Workload == JobWorkload.HeavyMedia
            ? _heavySlots
            : _lightSlots;
        bool acquired = false;
        try
        {
            await slots.WaitAsync(linkedCancellation.Token).ConfigureAwait(false);
            acquired = true;

            ToolValidationResult validation = await handler
                .ValidateAsync(request, linkedCancellation.Token)
                .ConfigureAwait(false);
            if (!validation.IsValid)
            {
                record.Fail(validation.Errors[0]);
                return record;
            }

            record.Start();
            var progress = new InlineProgress(record.Report);
            ToolExecutionResult result = await handler.ExecuteAsync(
                new JobExecutionContext(request, progress),
                linkedCancellation.Token).ConfigureAwait(false);

            if (result.PartialFailure is null)
            {
                record.Complete(result.OutputPaths);
            }
            else
            {
                record.CompletePartially(result.OutputPaths, result.PartialFailure);
            }

            return record;
        }
        catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
        {
            record.Cancel();
            return record;
        }
        catch (Exception exception)
        {
            record.Fail(new JobError("tool.unhandled", exception.Message, true));
            return record;
        }
        finally
        {
            if (acquired)
            {
                slots.Release();
            }

            _jobs.TryRemove(request.Id, out _);
            linkedCancellation.Dispose();
            running.Completion.TrySetResult();
        }
    }

    public bool TryCancel(Guid jobId)
    {
        if (!_jobs.TryGetValue(jobId, out RunningJob? running))
        {
            return false;
        }

        if (running.Record.Status == JobStatus.Running)
        {
            running.Record.RequestCancellation();
        }

        running.Cancellation.Cancel();
        return true;
    }

    public ValueTask DisposeAsync()
    {
        lock (_lifecycleGate)
        {
            if (_disposeTask is not null)
            {
                return new ValueTask(_disposeTask);
            }

            _disposed = true;
            _disposeTask = DrainAndDisposeAsync(_jobs.Values.ToArray());
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DrainAndDisposeAsync(IReadOnlyList<RunningJob> jobs)
    {
        foreach (RunningJob job in jobs)
        {
            job.Cancellation.Cancel();
        }

        await Task.WhenAll(jobs.Select(job => job.Completion.Task)).ConfigureAwait(false);
        _heavySlots.Dispose();
        _lightSlots.Dispose();
    }

    private sealed record RunningJob(
        JobRecord Record,
        CancellationTokenSource Cancellation,
        TaskCompletionSource Completion);

    private sealed class InlineProgress(Action<JobProgress> callback) : IProgress<JobProgress>
    {
        public void Report(JobProgress value) => callback(value);
    }
}
