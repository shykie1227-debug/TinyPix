using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TinyPix.App.ViewModels;
using TinyPix.Media.Ffmpeg;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace TinyPix.App.Controls;

/// <summary>
/// design: 唯一设置弹窗 (frame 7VTHb). One 820×620 card rendered inside the shell
/// scrim; it never creates a second dialog or a second window.
/// </summary>
public sealed partial class SettingsDialogContent : UserControl
{
    private const int DefaultHistoryLimit = 1000;

    private readonly Border[] _categories;
    private readonly FontIcon[] _categoryIcons;
    private readonly TextBlock[] _categoryLabels;
    private int _selected;

    public SettingsDialogContent(IServiceProvider services)
    {
        ViewModel = new SettingsDialogViewModel(services);
        InitializeComponent();

        _categories = new[] { CatGeneral, CatOutput, CatCache, CatEngine, CatLicense };
        _categoryIcons = new[] { CatGeneralIcon, CatOutputIcon, CatCacheIcon, CatEngineIcon, CatLicenseIcon };
        _categoryLabels = new[] { CatGeneralText, CatOutputText, CatCacheText, CatEngineText, CatLicenseText };

        SelectCategory(0);
        LoadEnginePaths(services);
        _ = InitializeAsync();
    }

    /// <summary>Raised when the user dismisses the dialog (close, cancel or save).</summary>
    public event EventHandler? CloseRequested;

    public SettingsDialogViewModel ViewModel { get; }

    private void LoadEnginePaths(IServiceProvider services)
    {
        IFfmpegEngineProvider? engine = services.GetService<IFfmpegEngineProvider>();
        FfmpegPathText.Text = engine?.FfmpegExecutablePath ?? "未找到内置引擎";
        FfprobePathText.Text = engine?.FfprobeExecutablePath ?? "未找到内置引擎";
    }

    private async Task InitializeAsync()
    {
        await ViewModel.LoadAsync();
        RefreshFromViewModel();
    }

    private void RefreshFromViewModel()
    {
        OutputDirectoryBox.Text = ViewModel.OutputDirectory;
        HistorySwitch.IsOn = ViewModel.HistoryLimit > 0;
        OverwriteSwitch.IsOn = !ViewModel.ConfirmBeforeReplacingExistingOutput;
        ReduceMotionSwitch.IsOn = ViewModel.ReduceMotion;
        CacheLimitBox.Value = Math.Round(ViewModel.MaximumCacheBytes / (1024.0 * 1024.0));

        Select(ThemeBox, ViewModel.Theme);
        Select(LanguageBox, ViewModel.Language);
    }

    private static void Select(ComboBox box, string tag)
    {
        for (int i = 0; i < box.Items.Count; i++)
        {
            if (box.Items[i] is ComboBoxItem item
                && string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                box.SelectedIndex = i;
                return;
            }
        }

        box.SelectedIndex = 0;
    }

    // ------------------------------------------------------------- categories

    private void OnCategoryTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is Border border
            && border.Tag is string tag
            && int.TryParse(tag, out int index))
        {
            SelectCategory(index);
        }
    }

    private void SelectCategory(int index)
    {
        _selected = index;

        var pageVisibility = new[]
        {
            PanelGeneral,
            PanelOutput,
            PanelCache,
            PanelEngine,
            PanelLicense,
        };

        for (int i = 0; i < _categories.Length; i++)
        {
            bool active = i == index;
            Border border = _categories[i];
            border.Background = Lookup(active ? "TinyPixLimeSoftBrush" : "TinyPixSurfaceMutedBrush");
            border.BorderBrush = Lookup(active ? "TinyPixLimeBrush" : "TinyPixSurfaceMutedBrush");
            border.BorderThickness = new Thickness(1);
            border.CornerRadius = new CornerRadius(10);
            border.Padding = new Thickness(12, 0, 12, 0);

            _categoryIcons[i].Foreground = Lookup(active ? "TinyPixDeepLimeBrush" : "TinyPixInkBrush");
            _categoryLabels[i].FontWeight = active
                ? Microsoft.UI.Text.FontWeights.Bold
                : Microsoft.UI.Text.FontWeights.Normal;
        }

        for (int i = 0; i < pageVisibility.Length; i++)
        {
            pageVisibility[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // -------------------------------------------------------------- commands

    private async void OnBrowseOutputClick(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add("*");

        if (MainWindow.Current is not null)
        {
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(MainWindow.Current));
        }

        Windows.Storage.StorageFolder folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            OutputDirectoryBox.Text = folder.Path;
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void OnCancelClick(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        ViewModel.OutputDirectory = OutputDirectoryBox.Text.Trim();
        ViewModel.Theme = (ThemeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "System";
        ViewModel.Language = (LanguageBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "zh-CN";
        ViewModel.ReduceMotion = ReduceMotionSwitch.IsOn;
        ViewModel.HistoryLimit = HistorySwitch.IsOn ? DefaultHistoryLimit : 0;
        ViewModel.ConfirmBeforeReplacingExistingOutput = !OverwriteSwitch.IsOn;
        ViewModel.MaximumCacheBytes = (long)Math.Round(CacheLimitBox.Value) * 1024L * 1024L;

        await ViewModel.SaveSettingsCommand.ExecuteAsync(null);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Used by the shell to keep the keyboard focus inside the card.</summary>
    public void FocusFirstCategory() => _categories[Math.Clamp(_selected, 0, _categories.Length - 1)].Focus(FocusState.Programmatic);

    private static Brush Lookup(string key) => (Brush)Application.Current.Resources[key];
}
