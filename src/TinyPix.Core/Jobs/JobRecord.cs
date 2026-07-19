namespace TinyPix.Core.Jobs;

public sealed class JobRecord
{
    private static readonly IReadOnlySet<JobStatus> TerminalStatuses = new HashSet<JobStatus>
    {
        JobStatus.Succeeded,
        JobStatus.Failed,
        JobStatus.PartialFailure,
        JobStatus.Interrupted,
        JobStatus.Cancelled,
    };

    private JobRecord(JobRequest request, JobStatus status)
    {
        Request = request;
        Status = status;
        CreatedUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id => Request.Id;

    public JobRequest Request { get; }

    public JobStatus Status { get; private set; }

    public double Progress { get; private set; }

    public string ProgressMessage { get; private set; } = string.Empty;

    public JobError? Error { get; private set; }

    public IReadOnlyList<string> OutputPaths { get; private set; } = [];

    public DateTimeOffset CreatedUtc { get; }

    public DateTimeOffset? StartedUtc { get; private set; }

    public DateTimeOffset? CompletedUtc { get; private set; }

    public bool IsTerminal => TerminalStatuses.Contains(Status);

    public static JobRecord CreateQueued(JobRequest request) => new(request, JobStatus.Queued);

    public static JobRecord CreateForRecovery(JobRequest request, JobStatus status)
    {
        if (status is not (JobStatus.Queued or JobStatus.Running or JobStatus.Cancelling))
        {
            throw new ArgumentOutOfRangeException(nameof(status), "Recovery factory accepts active states only.");
        }

        return new JobRecord(request, status);
    }

    public void Start()
    {
        Require(JobStatus.Queued);
        Status = JobStatus.Running;
        StartedUtc = DateTimeOffset.UtcNow;
    }

    public void Report(JobProgress progress)
    {
        Require(JobStatus.Running);
        Progress = progress.Percentage;
        ProgressMessage = progress.Message;
    }

    public void RequestCancellation()
    {
        Require(JobStatus.Running);
        Status = JobStatus.Cancelling;
        ProgressMessage = "正在安全停止";
    }

    public void Complete(IReadOnlyList<string> outputPaths)
    {
        Require(JobStatus.Running);
        OutputPaths = outputPaths;
        Progress = 100;
        Status = JobStatus.Succeeded;
        CompletedUtc = DateTimeOffset.UtcNow;
    }

    public void CompletePartially(IReadOnlyList<string> outputPaths, JobError error)
    {
        Require(JobStatus.Running);
        OutputPaths = outputPaths;
        Error = error;
        Status = JobStatus.PartialFailure;
        CompletedUtc = DateTimeOffset.UtcNow;
    }

    public void Fail(JobError error)
    {
        if (Status is not (JobStatus.Queued or JobStatus.Running or JobStatus.Cancelling))
        {
            throw InvalidTransition(JobStatus.Failed);
        }

        Error = error;
        Status = JobStatus.Failed;
        CompletedUtc = DateTimeOffset.UtcNow;
    }

    public void Cancel()
    {
        if (Status is not (JobStatus.Queued or JobStatus.Running or JobStatus.Cancelling))
        {
            throw InvalidTransition(JobStatus.Cancelled);
        }

        Status = JobStatus.Cancelled;
        CompletedUtc = DateTimeOffset.UtcNow;
    }

    public void Interrupt()
    {
        if (Status is not (JobStatus.Queued or JobStatus.Running or JobStatus.Cancelling))
        {
            throw InvalidTransition(JobStatus.Interrupted);
        }

        Error = new JobError("job.interrupted", "应用上次退出时任务尚未完成", true);
        Status = JobStatus.Interrupted;
        CompletedUtc = DateTimeOffset.UtcNow;
    }

    public JobRequest CreateRetryRequest()
    {
        if (!IsTerminal)
        {
            throw new InvalidOperationException("Only terminal jobs can be retried.");
        }

        return Request.CreateRetry();
    }

    private void Require(JobStatus expected)
    {
        if (Status != expected)
        {
            throw InvalidTransition(expected);
        }
    }

    private InvalidOperationException InvalidTransition(JobStatus target) =>
        new($"Job {Id} cannot transition from {Status} to {target}.");
}

public static class JobRecovery
{
    public static void MarkInterruptedAfterRestart(IEnumerable<JobRecord> jobs)
    {
        foreach (JobRecord job in jobs.Where(job => !job.IsTerminal))
        {
            job.Interrupt();
        }
    }
}
