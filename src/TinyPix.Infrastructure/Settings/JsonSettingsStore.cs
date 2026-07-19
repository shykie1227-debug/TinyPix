using System.Text.Json;
using TinyPix.Core.Settings;

namespace TinyPix.Infrastructure.Settings;

public sealed class JsonSettingsStore(
    string settingsPath,
    TinyPixSettings defaults) : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _settingsPath = Path.GetFullPath(settingsPath);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<TinyPixSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return defaults;
            }

            await using FileStream stream = File.OpenRead(_settingsPath);
            TinyPixSettings? settings = await JsonSerializer.DeserializeAsync<TinyPixSettings>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
            return settings ?? throw new InvalidDataException("settings.json 内容为空。");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        TinyPixSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? temporaryPath = null;
        try
        {
            string directory = Path.GetDirectoryName(_settingsPath)
                ?? throw new InvalidOperationException("设置文件缺少父目录。");
            Directory.CreateDirectory(directory);
            temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_settingsPath)}.{Guid.NewGuid():N}.tmp");
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    settings,
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, _settingsPath, overwrite: true);
            temporaryPath = null;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                File.Delete(temporaryPath);
            }

            _gate.Release();
        }
    }
}
