using TinyPix.Core.Jobs;

namespace TinyPix.Core.Tests;

public sealed class JobRecordTests
{
    [Fact]
    public void Cancellation_uses_explicit_cancelling_state_before_cancelled()
    {
        JobRecord job = JobRecord.CreateQueued(Request());

        job.Start();
        job.RequestCancellation();
        job.Cancel();

        Assert.Equal(JobStatus.Cancelled, job.Status);
        Assert.True(job.IsTerminal);
    }

    [Fact]
    public void Completing_a_job_that_never_started_is_rejected()
    {
        JobRecord job = JobRecord.CreateQueued(Request());

        Assert.Throws<InvalidOperationException>(() => job.Complete(["output.mp4"]));
    }

    [Fact]
    public void Retry_creates_a_new_identity_without_mutating_the_old_record()
    {
        JobRecord failed = JobRecord.CreateQueued(Request());
        failed.Start();
        failed.Fail(new JobError("engine.failed", "引擎执行失败", true));

        JobRequest retry = failed.CreateRetryRequest();

        Assert.NotEqual(failed.Id, retry.Id);
        Assert.Equal(failed.Request.ToolId, retry.ToolId);
        Assert.Equal(failed.Request.InputPaths, retry.InputPaths);
        Assert.Equal(JobStatus.Failed, failed.Status);
    }

    [Theory]
    [InlineData(JobStatus.Queued)]
    [InlineData(JobStatus.Running)]
    [InlineData(JobStatus.Cancelling)]
    public void Restart_marks_non_terminal_work_as_interrupted(JobStatus state)
    {
        JobRecord job = JobRecord.CreateForRecovery(Request(), state);

        JobRecovery.MarkInterruptedAfterRestart([job]);

        Assert.Equal(JobStatus.Interrupted, job.Status);
        Assert.True(job.IsTerminal);
    }

    private static JobRequest Request() => JobRequest.Create(
        "video.convert",
        ["input.mp4"],
        "output",
        JobWorkload.HeavyMedia);
}
