using System;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TinyPix.App.Services;
using TinyPix.App.ViewModels;
using TinyPix.Core.Tools;

namespace TinyPix.App.Controls;

/// <summary>
/// Shell/LeftFilePanel — left file region of the workbench. Raises
/// <see cref="ToolInvoked"/> when a tool row is clicked and
/// <see cref="AddFilesRequested"/> when the primary add button is pressed, so
/// the shell can forward both to the active page.
/// </summary>
public sealed partial class LeftFilePanelControl : UserControl
{
    private MainWindowViewModel? _viewModel;

    /// <summary>The live instance, set on load so pages can mirror their selection.</summary>
    public static LeftFilePanelControl? Current { get; private set; }

    public LeftFilePanelControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        ToolList.SelectionChanged += OnToolSelectionChanged;
        AddFilesButton.Click += (_, _) => AddFilesRequested?.Invoke(this, EventArgs.Empty);
        Loaded += (_, _) => Current = this;
        Unloaded += (_, _) =>
        {
            if (ReferenceEquals(Current, this))
            {
                Current = null;
            }
        };
    }

    /// <summary>Raised with the tool id a left-panel row selected.</summary>
    public event EventHandler<string>? ToolInvoked;

    /// <summary>Raised when the user asks to add media files.</summary>
    public event EventHandler? AddFilesRequested;

    /// <summary>Updates the file card from the active page selection.</summary>
    public void ShowFile(string? name, string? meta, string? badge)
    {
        FileNameText.Text = string.IsNullOrWhiteSpace(name) ? "尚未添加文件" : name;
        FileMetaText.Text = string.IsNullOrWhiteSpace(meta) ? "选择文件后在此显示尺寸与状态" : meta;
        FileBadgeText.Text = string.IsNullOrWhiteSpace(badge) ? "等待添加" : badge;
        FileCountText.Text = string.IsNullOrWhiteSpace(name) ? "0 个文件" : "1 个文件";
    }

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = args.NewValue as MainWindowViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        RefreshTools();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.VisibleTools)
            || e.PropertyName == nameof(MainWindowViewModel.SelectedCategory))
        {
            RefreshTools();
        }
    }

    private void RefreshTools()
    {
        ToolList.ItemsSource = _viewModel?.VisibleTools;
    }

    private void OnToolSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ToolList.SelectedItem is ToolDescriptor tool)
        {
            // Let the same row be picked again later.
            ToolList.SelectedItem = null;

            // Route on the next dispatcher tick instead of inline: the handler
            // chain switches the active category, which rebuilds this very
            // ListView's ItemsSource. Doing that while the ListView is still
            // dispatching SelectionChanged re-enters the control and crashes the
            // process with E_POINTER (0x80004003).
            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    ToolInvoked?.Invoke(this, tool.Id);
                }
                catch (Exception ex)
                {
                    // An exception escaping a dispatcher callback is turned into
                    // a fail-fast inside CoreMessagingXP with no managed stack,
                    // so capture it here where the frames are still available.
                    StartupTrace.Write($"ToolInvoked({tool.Id}) THREW: {ex}");
                    throw;
                }
            });
        }
    }
}
