using TinyPix.Core.History;
using TinyPix.Core.Jobs;
using TinyPix.Infrastructure.History;

namespace TinyPix.Infrastructure.Tests;

public sealed class SqliteHistoryRepositoryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"tinypix-history-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task Recent_files_are_deduplicated_and_pruned_to_the_configured_limit()
    {
        var repository = Repository(recentLimit: 3, historyLimit: 4);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        for (int index = 0; index < 4; index++)
        {
            await repository.AddRecentFileAsync(new RecentFileEntry(
                Path.Combine(_root, $"file-{index}.mp4"),
                "video.convert",
                now.AddMinutes(index)));
        }

        await repository.AddRecentFileAsync(new RecentFileEntry(
            Path.Combine(_root, "FILE-2.MP4"),
            "video.trim",
            now.AddMinutes(10)));
        IReadOnlyList<RecentFileEntry> recent = await repository.GetRecentFilesAsync();

        Assert.Equal(3, recent.Count);
        Assert.Equal("video.trim", recent[0].LastToolId);
        Assert.DoesNotContain(recent, entry => entry.Path.EndsWith("file-0.mp4"));
    }

    [Fact]
    public async Task History_is_pruned_and_active_jobs_become_interrupted_after_restart()
    {
        var repository = Repository(recentLimit: 3, historyLimit: 4);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        for (int index = 0; index < 5; index++)
        {
            await repository.SaveHistoryAsync(new HistoryEntry(
                Guid.NewGuid(),
                "file.hash",
                index == 4 ? JobStatus.Running : JobStatus.Succeeded,
                [Path.Combine(_root, $"input-{index}.bin")],
                [],
                new Dictionary<string, string> { ["algorithm"] = "sha256" },
                null,
                now.AddMinutes(index),
                index == 4 ? null : now.AddMinutes(index + 1)));
        }

        await repository.MarkActiveJobsInterruptedAsync();
        IReadOnlyList<HistoryEntry> history = await repository.GetHistoryAsync();

        Assert.Equal(4, history.Count);
        Assert.Equal(JobStatus.Interrupted, history[0].Status);
        Assert.Equal("job.interrupted", history[0].Error?.Code);
        Assert.DoesNotContain(history, entry => entry.InputPaths[0].EndsWith("input-0.bin"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private SqliteHistoryRepository Repository(int recentLimit, int historyLimit) =>
        new(Path.Combine(_root, "Data", "tinypix.db"), recentLimit, historyLimit);
}
