namespace TinyPix.Core.Cache;

public sealed record CacheSummary(long TotalBytes, int FileCount, DateTimeOffset MeasuredUtc);

public sealed record CacheClearResult(long DeletedBytes, int DeletedFiles, IReadOnlyList<string> FailedPaths);

public interface ICacheService
{
    Task<CacheSummary> MeasureAsync(CancellationToken cancellationToken = default);

    Task<CacheClearResult> ClearAsync(CancellationToken cancellationToken = default);
}
