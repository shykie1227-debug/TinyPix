using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TinyPix.Core.Cache;

namespace TinyPix.App.Services;

/// <summary>
/// File-system ICacheService over the portable workspace Cache directory. Source
/// media is never touched; only derived artifacts under Cache/ are measured and
/// cleared. Deletes are best-effort and report failures instead of throwing.
/// </summary>
public sealed class LocalFileCacheService(string cacheDirectory) : ICacheService
{
    private readonly string _cacheDirectory = Path.GetFullPath(cacheDirectory);

    public Task<CacheSummary> MeasureAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_cacheDirectory))
        {
            return Task.FromResult(new CacheSummary(0, 0, DateTimeOffset.UtcNow));
        }

        long totalBytes = 0;
        int fileCount = 0;
        foreach (string path in Directory.EnumerateFiles(
                     _cacheDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                totalBytes += new FileInfo(path).Length;
                fileCount += 1;
            }
            catch (IOException)
            {
                // A file vanishing mid-scan must not fail the whole measurement.
            }
        }

        return Task.FromResult(new CacheSummary(totalBytes, fileCount, DateTimeOffset.UtcNow));
    }

    public Task<CacheClearResult> ClearAsync(CancellationToken cancellationToken = default)
    {
        long deletedBytes = 0;
        int deletedFiles = 0;
        var failedPaths = new List<string>();

        if (Directory.Exists(_cacheDirectory))
        {
            foreach (string path in Directory.EnumerateFiles(
                         _cacheDirectory, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    long length = new FileInfo(path).Length;
                    File.Delete(path);
                    deletedBytes += length;
                    deletedFiles += 1;
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                    failedPaths.Add(path);
                }
            }
        }

        return Task.FromResult(new CacheClearResult(
            deletedBytes,
            deletedFiles,
            failedPaths.Count == 0
                ? Array.Empty<string>()
                : failedPaths.ToArray()));
    }
}
