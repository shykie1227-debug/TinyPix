using TinyPix.Core.Jobs;

namespace TinyPix.Core.History;

public sealed record RecentFileEntry(
    string Path,
    string? LastToolId,
    DateTimeOffset LastOpenedUtc);

public sealed record HistoryEntry(
    Guid JobId,
    string ToolId,
    JobStatus Status,
    IReadOnlyList<string> InputPaths,
    IReadOnlyList<string> OutputPaths,
    IReadOnlyDictionary<string, string> Parameters,
    JobError? Error,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? CompletedUtc);

public interface IHistoryRepository
{
    Task AddRecentFileAsync(RecentFileEntry entry, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecentFileEntry>> GetRecentFilesAsync(
        int limit = 200,
        CancellationToken cancellationToken = default);

    Task SaveHistoryAsync(HistoryEntry entry, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HistoryEntry>> GetHistoryAsync(
        int limit = 1000,
        CancellationToken cancellationToken = default);

    Task MarkActiveJobsInterruptedAsync(CancellationToken cancellationToken = default);
}
