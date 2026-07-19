using TinyPix.Core.Settings;
using TinyPix.Infrastructure.Settings;

namespace TinyPix.Infrastructure.Tests;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"tinypix-settings-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task Missing_settings_return_defaults_without_writing_outside_portable_path()
    {
        string path = Path.Combine(_root, "Config", "settings.json");
        TinyPixSettings defaults = TinyPixSettings.Default(Path.Combine(_root, "Output"));
        var store = new JsonSettingsStore(path, defaults);

        TinyPixSettings loaded = await store.LoadAsync();

        Assert.Equal(defaults, loaded);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Settings_are_saved_atomically_and_can_be_loaded()
    {
        string path = Path.Combine(_root, "Config", "settings.json");
        TinyPixSettings defaults = TinyPixSettings.Default(Path.Combine(_root, "Output"));
        var store = new JsonSettingsStore(path, defaults);
        TinyPixSettings changed = defaults with { Theme = "Dark", MaximumCacheBytes = 1234 };

        await store.SaveAsync(changed);
        TinyPixSettings loaded = await store.LoadAsync();

        Assert.Equal(changed, loaded);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
