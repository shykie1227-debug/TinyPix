using TinyPix.Core.Jobs;
using TinyPix.Core.Tools;

namespace TinyPix.Core.Tests;

public sealed class JobQueueServiceTests
{
    [Theory]
    [InlineData(JobWorkload.HeavyMedia, 1)]
    [InlineData(JobWorkload.LightFile, 2)]
    public async Task Queue_enforces_workload_concurrency(
        JobWorkload workload,
        int expectedMaximum)
    {
        await using var queue = new JobQueueService();
        string toolId = workload == JobWorkload.HeavyMedia ? "video.convert" : "file.hash";
        var handler = new ProbeHandler(toolId, TimeSpan.FromMilliseconds(80));
        Task<JobRecord>[] jobs = Enumerable.Range(0, 5)
            .Select(index => queue.RunAsync(Request(index, workload), handler))
            .ToArray();

        JobRecord[] results = await Task.WhenAll(jobs);

        Assert.All(results, result => Assert.Equal(JobStatus.Succeeded, result.Status));
        Assert.Equal(expectedMaximum, handler.MaximumConcurrency);
    }

    [Fact]
    public async Task A_failed_job_does_not_poison_the_next_job()
    {
        await using var queue = new JobQueueService();
        var failing = new ProbeHandler("file.hash", TimeSpan.Zero, fail: true);
        var succeeding = new ProbeHandler("file.hash", TimeSpan.Zero);

        JobRecord failed = await queue.RunAsync(Request(1, JobWorkload.LightFile), failing);
        JobRecord succeeded = await queue.RunAsync(Request(2, JobWorkload.LightFile), succeeding);

        Assert.Equal(JobStatus.Failed, failed.Status);
        Assert.Equal("tool.unhandled", failed.Error?.Code);
        Assert.Equal(JobStatus.Succeeded, succeeded.Status);
    }

    [Fact]
    public async Task Cancellation_stops_only_the_target_job()
    {
        await using var queue = new JobQueueService();
        var handler = new ProbeHandler("video.convert", TimeSpan.FromSeconds(10));
        JobRequest request = Request(1, JobWorkload.HeavyMedia);
        Task<JobRecord> running = queue.RunAsync(request, handler);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(queue.TryCancel(request.Id));
        JobRecord result = await running;

        Assert.Equal(JobStatus.Cancelled, result.Status);
    }

    [Fact]
    public async Task Dispose_cancels_and_drains_active_jobs_before_releasing_resources()
    {
        var queue = new JobQueueService();
        var handler = new ProbeHandler("video.convert", TimeSpan.FromSeconds(10));
        JobRequest request = Request(1, JobWorkload.HeavyMedia);
        Task<JobRecord> running = queue.RunAsync(request, handler);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await queue.DisposeAsync();
        JobRecord result = await running;

        Assert.Equal(JobStatus.Cancelled, result.Status);
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            queue.RunAsync(Request(2, JobWorkload.HeavyMedia), handler));
    }

    private static JobRequest Request(int index, JobWorkload workload) => JobRequest.Create(
        workload == JobWorkload.HeavyMedia ? "video.convert" : "file.hash",
        [$"input-{index}.bin"],
        "output",
        workload);

    private sealed class ProbeHandler(
        string toolId,
        TimeSpan delay,
        bool fail = false) : IToolHandler
    {
        private int _concurrency;
        private int _maximumConcurrency;

        public string ToolId { get; } = toolId;

        public int MaximumConcurrency => Volatile.Read(ref _maximumConcurrency);

        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<ToolValidationResult> ValidateAsync(
            JobRequest request,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(ToolValidationResult.Valid());

        public ValueTask<ToolEstimate> EstimateAsync(
            JobRequest request,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ToolEstimate(null, null));

        public async Task<ToolExecutionResult> ExecuteAsync(
            JobExecutionContext context,
            CancellationToken cancellationToken)
        {
            int current = Interlocked.Increment(ref _concurrency);
            int observed;
            do
            {
                observed = Volatile.Read(ref _maximumConcurrency);
            }
            while (current > observed &&
                   Interlocked.CompareExchange(ref _maximumConcurrency, current, observed) != observed);

            Started.TrySetResult();
            try
            {
                await Task.Delay(delay, cancellationToken);
                if (fail)
                {
                    throw new InvalidOperationException("probe failure");
                }

                return ToolExecutionResult.Success([$"{context.Request.Id}.out"]);
            }
            finally
            {
                Interlocked.Decrement(ref _concurrency);
            }
        }
    }
}
