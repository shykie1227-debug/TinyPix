using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TinyPix.App.Controls;
using TinyPix.App.Pages;
using TinyPix.App.ViewModels;
using TinyPix.Core.Jobs;
using Windows.System;
using Windows.UI.Core;

namespace TinyPix.App;

/// <summary>
/// The single native shell window. Hosts the top category NavigationView, the
/// four-region workbench (file panel / workbench frame / parameter panel /
/// task queue) and owns shell-level keyboard boundaries:
/// F6 cycles the five focus regions, Ctrl+J jumps to the task queue,
/// J/K/L drive timeline transport, Ctrl+, opens the one SettingsDialog.
/// </summary>
public sealed partial class MainWindow : Window
{
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

        // First launch lands on 视频工具 > 视频输出 (design/UI-SPEC.md).
        CategoryNav.SelectedItem = VideoCategoryItem;
        WorkbenchFrame.Navigate(typeof(VideoOutputPage), _viewModel);
    }

    private void OnCategorySelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            // The settings entry opens the modal SettingsDialog instead of
            // navigating, so the selected category stays unchanged.
            _ = OpenSettingsDialogAsync();
            return;
        }

        if (args.SelectedItem is NavigationViewItem item)
        {
            _viewModel.SelectCategory(item.Tag as string);
            if (!WorkbenchFrame.Navigate(typeof(VideoOutputPage), _viewModel))
            {
                System.Diagnostics.Debug.WriteLine("Workbench navigation failed.");
            }
        }
    }

    private void OnCategoryItemInvoked(
        NavigationView sender,
        NavigationViewItemInvokedEventArgs args)
    {
        if (args.IsSettingsInvoked)
        {
            _ = OpenSettingsDialogAsync();
        }
    }

    private async System.Threading.Tasks.Task OpenSettingsDialogAsync()
    {
        var dialog = new ContentDialog
        {
            Title = "设置",
            Content = new SettingsDialogContent(App.Services),
            CloseButtonText = "关闭",
            DefaultButton = ContentDialogButton.Close,
            // The modal scrim must cover the complete XamlRoot, top nav included.
            XamlRoot = ShellRoot.XamlRoot,
        };
        await dialog.ShowAsync();
    }

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
            // F6 cycles 顶部导航 → 左文件栏 → 中央区 → 右参数栏 → 任务队列,
            // Shift+F6 walks the same five regions in reverse.
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
                SeekTimelineBackward();
            }
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.K)
        {
            ToggleTimelinePlayback();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.L)
        {
            SeekTimelineForward();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.OemComma && controlDown)
        {
            _ = OpenSettingsDialogAsync();
            e.Handled = true;
        }
    }

    private void CycleFocusRegion(bool reverse)
    {
        _focusRegions ??= new UIElement[]
        {
            CategoryNav,
            LeftFilePanel,
            WorkbenchFrame,
            RightParameterPanel,
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
        _focusRegionIndex = 4;
    }

    // Timeline transport placeholders; the timeline workbench pages (video.trim
    // and friends) subscribe to the same J/K/L contract when they land.
    private void SeekTimelineBackward() => _viewModel.TransportSeek(-1);

    private void ToggleTimelinePlayback() => _viewModel.TransportToggle();

    private void SeekTimelineForward() => _viewModel.TransportSeek(+1);
}
