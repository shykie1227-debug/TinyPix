using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using TinyPix.Core.Cache;
using TinyPix.Core.Settings;

namespace TinyPix.App.ViewModels;

/// <summary>
/// View model for the single reusable SettingsDialog. Loads TinyPixSettings from
/// the ISettingsStore, exposes editable mirrors, and persists through
/// SaveSettingsCommand with an atomic JSON write under the portable workspace.
/// </summary>
public partial class SettingsDialogViewModel : ObservableObject
{
    private readonly ISettingsStore _settingsStore;
    private readonly ICacheService _cacheService;
    private TinyPixSettings? _loaded;

    public SettingsDialogViewModel(IServiceProvider services)
    {
        _settingsStore = services.GetRequiredService<ISettingsStore>();
        _cacheService = services.GetRequiredService<ICacheService>();
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync);
    }

    [ObservableProperty]
    private string _outputDirectory = string.Empty;

    [ObservableProperty]
    private string _theme = "System";

    [ObservableProperty]
    private string _language = "zh-CN";

    [ObservableProperty]
    private bool _reduceMotion;

    [ObservableProperty]
    private long _maximumCacheBytes;

    [ObservableProperty]
    private bool _confirmBeforeReplacingExistingOutput = true;

    public IAsyncRelayCommand SaveSettingsCommand { get; }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _loaded = await _settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        OutputDirectory = _loaded.OutputDirectory;
        Theme = _loaded.Theme;
        Language = _loaded.Language;
        ReduceMotion = _loaded.ReduceMotion;
        MaximumCacheBytes = _loaded.MaximumCacheBytes;
        ConfirmBeforeReplacingExistingOutput = _loaded.ConfirmBeforeReplacingExistingOutput;
    }

    private async Task SaveSettingsAsync(CancellationToken cancellationToken)
    {
        TinyPixSettings baseline =
            _loaded ?? await _settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        TinyPixSettings updated = baseline with
        {
            OutputDirectory = OutputDirectory,
            Theme = Theme,
            Language = Language,
            ReduceMotion = ReduceMotion,
            MaximumCacheBytes = MaximumCacheBytes,
            ConfirmBeforeReplacingExistingOutput = ConfirmBeforeReplacingExistingOutput,
        };
        await _settingsStore.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
        _loaded = updated;
    }
}
