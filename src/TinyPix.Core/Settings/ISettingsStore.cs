using System.Text.Json.Serialization;

namespace TinyPix.Core.Settings;

public sealed record TinyPixSettings(
    int SchemaVersion,
    string OutputDirectory,
    [property: JsonPropertyName("cacheLimitBytes")]
    long MaximumCacheBytes,
    string Theme,
    string Language,
    bool ReduceMotion,
    int RecentFileLimit,
    int HistoryLimit,
    bool ConfirmBeforeReplacingExistingOutput)
{
    public static TinyPixSettings Default(string outputDirectory) => new(
        1,
        outputDirectory,
        5L * 1024 * 1024 * 1024,
        "System",
        "zh-CN",
        false,
        200,
        1000,
        true);
}

public interface ISettingsStore
{
    Task<TinyPixSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(TinyPixSettings settings, CancellationToken cancellationToken = default);
}
