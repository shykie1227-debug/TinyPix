using TinyPix.Media.Ffmpeg;

namespace TinyPix.Media.Tests;

public sealed class FfmpegProcessRunnerTests
{
    [Fact]
    public async Task Runner_streams_progress_and_returns_exit_code()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var runner = new FfmpegProcessRunner();
        var progress = new List<FfmpegProgress>();

        FfmpegProcessResult result = await runner.RunAsync(
            "/bin/sh",
            ["-c", "printf 'out_time_us=500000\\nprogress=continue\\nprogress=end\\n'"],
            TimeSpan.FromSeconds(1),
            new InlineProgress(value => progress.Add(value)));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(2, progress.Count);
        Assert.Equal(50, progress[0].Percentage);
        Assert.True(progress[1].IsComplete);
    }

    [Fact]
    public async Task Cancellation_terminates_the_process_tree()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var runner = new FfmpegProcessRunner();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(
            "/bin/sh",
            ["-c", "sleep 10"],
            null,
            null,
            cancellation.Token));
    }

    private sealed class InlineProgress(Action<FfmpegProgress> callback) : IProgress<FfmpegProgress>
    {
        public void Report(FfmpegProgress value) => callback(value);
    }
}
