using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using TinyPix.App.Controls;
using TinyPix.App.Pages;
using TinyPix.App.Services;
using TinyPix.App.ViewModels;
using TinyPix.Core.Jobs;
using Windows.System;
using Windows.UI.Core;

namespace TinyPix.App;

/// <summary>
/// The single native shell window. Hosts the design's Top Navigation (brand +
/// three mode pills + offline chip + settings), the workbench body
/// (left file panel 232 / category page) and the bottom task queue.
/// Keyboard boundaries: F6 cycles the four focus regions, Ctrl+J jumps to the
/// task queue, J/K/L drive timeline transport, Ctrl+, opens SettingsDialog.
/// </summary>
public sealed partial class MainWindow : Window
{
    // Windows.System.VirtualKey has no OEM members (the enum stops at the
    // Gamepad* values), so Ctrl+, is matched through its Win32 code: VK_OEM_COMMA.
    private const int VkOemComma = 188;

    // `new` is required: Window already exposes a static Current.
    internal static new MainWindow? Current { get; private set; }

    private readonly MainWindowViewModel _viewModel;
    private readonly JobQueueService _jobQueue;
    private IReadOnlyList<UIElement>? _focusRegions;
    private int _focusRegionIndex = -1;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainWindowViewModel(App.Services);
        _jobQueue = App.Services.GetRequiredService<JobQueueService>();
        ShellRoot.PreviewKeyDown += OnShellPreviewKeyDown;
        LeftFilePanel.DataContext = _viewModel;
        LeftFilePanel.ToolInvoked += OnToolInvoked;
        LeftFilePanel.AddFilesRequested += OnAddFilesRequested;

        // First launch lands on 视频工具 > 视频输出 (design template 1).
        SetActiveMode("video");
        WorkbenchFrame.Navigate(typeof(VideoOutputPage), _viewModel);
        Current = this;
    }

    // ---------------------------------------------------------------- modes

    private void SetActiveMode(string mode)
    {
        _viewModel.SelectCategory(mode);
        ApplyModeVisuals(mode);

        // A ToggleButton flips its own IsChecked as part of raising Click, which
        // can land after the assignments above and leave the pill showing the
        // previous category. Re-assert the visual state on the next tick, once
        // the control has finished its own click bookkeeping.
        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()
            ?.TryEnqueue(() => ApplyModeVisuals(mode));
    }

    private void ApplyModeVisuals(string mode)
    {
        SetPillActive(ImageModeButton, string.Equals(mode, "image", StringComparison.Ordinal));
        SetPillActive(VideoModeButton, string.Equals(mode, "video", StringComparison.Ordinal));
        SetPillActive(ToolboxModeButton, string.Equals(mode, "toolbox", StringComparison.Ordinal));
    }

    /// <summary>
    /// Applies the pill's selected look directly instead of relying on the
    /// template's CheckStates group: ToggleButton only drives those visual states
    /// for its own toggle bookkeeping, so a programmatic IsChecked assignment was
    /// leaving the previous pill highlighted after a category switch.
    /// </summary>
    private static void SetPillActive(ToggleButton pill, bool active)
    {
        pill.IsChecked = active;
        pill.Background = active
            ? (Brush)Application.Current.Resources["TinyPixLimeBrush"]
            : null;
        pill.FontWeight = active
            ? Microsoft.UI.Text.FontWeights.Bold
            : Microsoft.UI.Text.FontWeights.SemiBold;
    }

    private void OnImageModeClick(object sender, RoutedEventArgs e) => SwitchMode("image", typeof(ImageWorkbenchPage));

    private void OnVideoModeClick(object sender, RoutedEventArgs e) => SwitchMode("video", typeof(VideoOutputPage));

    private void OnToolboxModeClick(object sender, RoutedEventArgs e) => SwitchMode("toolbox", typeof(PdfPage));

    private void SwitchMode(string mode, Type page)
    {
        if (string.Equals(_viewModel.SelectedCategory, mode, StringComparison.Ordinal)
            && WorkbenchFrame.CurrentSourcePageType == page)
        {
            // Re-clicking the active pill must not uncheck it.
            SetActiveMode(mode);
            return;
        }

        SetActiveMode(mode);
        Navigate(page);
    }

    private void OnAddFilesRequested(object? sender, EventArgs e)
    {
        if (WorkbenchFrame.Content is IWorkbenchFilePicker picker)
        {
            _ = picker.AddFilesAsync();
        }
    }

    private void OnToolInvoked(object? sender, string toolId)
    {
        Type page = toolId switch
        {
            "video.gif" => typeof(GifMakerPage),
            "video.trim" or "video.split" or "video.merge" or "video.geometry"
                or "video.speed-volume" or "video.audio" or "video.overlay"
                or "video.frames" or "video.metadata" => typeof(VideoEditorPage),
            "image.background" or "image.batch" or "image.collage" or "image.color"
                or "image.compress" or "image.convert" or "image.crop" or "image.exif"
                or "image.resize" or "image.transform" or "image.watermark" => typeof(ImageWorkbenchPage),
            var id when id.StartsWith("pdf.", StringComparison.Ordinal) => typeof(PdfPage),
            "code.scan" or "ocr.image" or "ocr.pdf" => typeof(OcrQrPage),
            "code.generate" or "file.hash" or "file.rename" or "file.metadata"
                or "file.clipboard-image" or "file.batch-history" => typeof(GeneratorsPage),
            var id when id.StartsWith("idphoto.", StringComparison.Ordinal) => typeof(IdPhotoPage),
            _ => typeof(VideoOutputPage),
        };

        // Keep the mode pill in sync with the page the tool belongs to.
        if (toolId.StartsWith("image.", StringComparison.Ordinal))
        {
            SetActiveMode("image");
        }
        else if (toolId.StartsWith("video.", StringComparison.Ordinal))
        {
            SetActiveMode("video");
        }
        else
        {
            SetActiveMode("toolbox");
        }

        Navigate(page, toolId);
    }

    private void Navigate(Type page, string? toolId = null)
    {
        object parameter = toolId is null ? _viewModel : new WorkbenchNavigation(_viewModel, toolId);
        try
        {
            if (WorkbenchFrame.Navigate(page, parameter, new SuppressNavigationTransitionInfo()))
            {
                StartupTrace.Write($"navigate {page.Name} ok (tool={toolId ?? "<none>"})");
            }
            else
            {
                StartupTrace.Write($"navigate {page.Name} returned false (tool={toolId ?? "<none>"})");
            }
        }
        catch (Exception ex)
        {
            StartupTrace.Write($"navigate {page.Name} THREW (tool={toolId ?? "<none>"}): {ex}");
            throw;
        }
    }

    // ------------------------------------------------------------- settings

    private void OnSettingsClick(object sender, RoutedEventArgs e) => ShowSettings();

    private void ShowSettings()
    {
        if (SettingsOverlay.Visibility == Visibility.Visible)
        {
            return;
        }

        var content = new SettingsDialogContent(App.Services);
        content.CloseRequested += OnSettingsCloseRequested;
        SettingsHost.Content = content;
        SettingsOverlay.Visibility = Visibility.Visible;
        content.FocusFirstCategory();
    }

    private void OnSettingsCloseRequested(object? sender, EventArgs e) => HideSettings();

    private void HideSettings()
    {
        SettingsOverlay.Visibility = Visibility.Collapsed;
        if (SettingsHost.Content is SettingsDialogContent content)
        {
            content.CloseRequested -= OnSettingsCloseRequested;
        }

        SettingsHost.Content = null;
        ModeBar.Focus(FocusState.Programmatic);
    }

    // ------------------------------------------------------------- keyboard

    private void OnShellPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        CoreVirtualKeyStates controlState =
            InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
        CoreVirtualKeyStates shiftState =
            InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        bool controlDown = controlState.HasFlag(CoreVirtualKeyStates.Down);
        bool shiftDown = shiftState.HasFlag(CoreVirtualKeyStates.Down);

        if (e.Key == VirtualKey.F6)
        {
            // F6 cycles 顶部导航 → 左文件栏 → 中央区 → 任务队列;
            // Shift+F6 walks the same regions in reverse.
            CycleFocusRegion(reverse: shiftDown);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.J)
        {
            if (controlDown)
            {
                FocusTaskQueue();
            }
            else
            {
                _viewModel.TransportSeek(-1);
            }

            e.Handled = true;
        }
        else if (e.Key == VirtualKey.K)
        {
            _viewModel.TransportToggle();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.L)
        {
            _viewModel.TransportSeek(+1);
            e.Handled = true;
        }
        else if ((int)e.Key == VkOemComma && controlDown)
        {
            ShowSettings();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape && SettingsOverlay.Visibility == Visibility.Visible)
        {
            HideSettings();
            e.Handled = true;
        }
    }

    private void CycleFocusRegion(bool reverse)
    {
        _focusRegions ??= new UIElement[]
        {
            ModeBar,
            LeftFilePanel,
            WorkbenchFrame,
            TaskQueue,
        };

        int count = _focusRegions.Count;
        _focusRegionIndex = reverse
            ? (_focusRegionIndex <= 0 ? count - 1 : _focusRegionIndex - 1)
            : (_focusRegionIndex + 1) % count;
        _focusRegions[_focusRegionIndex].Focus(FocusState.Keyboard);
    }

    private void FocusTaskQueue()
    {
        TaskQueue.Focus(FocusState.Keyboard);
        _focusRegionIndex = 3;
    }
}

/// <summary>
/// Navigation parameter for workbench pages: the shell view model plus the tool
/// a left-panel entry asked for, so a page can preselect that tool.
/// </summary>
public sealed class WorkbenchNavigation
{
    public WorkbenchNavigation(MainWindowViewModel viewModel, string toolId)
    {
        ViewModel = viewModel;
        ToolId = toolId;
    }

    public MainWindowViewModel ViewModel { get; }

    public string ToolId { get; }
}
