using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;
using TinyPix.App.Controls;
using TinyPix.App.Services;
using TinyPix.App.ViewModels;
using TinyPix.Core.Jobs;
using TinyPix.Media.IdPhoto;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace TinyPix.App.Pages;

/// <summary>
/// Template 4 / 智能证件照 — design frame cQnR3. Center preview + 常用证件模板,
/// right 证件照规格 panel driving idphoto.create / check / print.
/// </summary>
public sealed partial class IdPhotoPage : Page, IWorkbenchFilePicker
{
    private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".webp", ".tif", ".tiff" };

    private readonly List<string> _inputPaths = new();
    private JobSubmissionService? _submission;
    private string _toolId = "idphoto.create";

    public IdPhotoPage()
    {
        InitializeComponent();
        Presets.SetPresets(new[]
        {
            new PresetCard("\uE77B", "一寸", "25 × 35 mm", true),
            new PresetCard("\uE77B", "二寸", "35 × 49 mm"),
            new PresetCard("\uE774", "护照", "33 × 48 mm"),
            new PresetCard("\uE710", "自定义预设", "保存当前参数"),
        });

        Presets.PresetInvoked += OnPresetInvoked;
        SyncSpecRows();
    }

    public MainWindowViewModel? ViewModel { get; private set; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is WorkbenchNavigation navigation)
        {
            ViewModel = navigation.ViewModel;
            _toolId = navigation.ToolId is { Length: > 0 } id ? id : "idphoto.create";
        }
        else
        {
            ViewModel = e.Parameter as MainWindowViewModel;
        }

        _submission = App.Services.GetRequiredService<JobSubmissionService>();
    }

    private void OnPresetInvoked(object? sender, int index)
    {
        if (index is >= 0 and <= 2)
        {
            PresetBox.SelectedIndex = index;
        }
    }

    private void OnPresetChanged(object sender, SelectionChangedEventArgs e) => SyncSpecRows();

    private void SyncSpecRows()
    {
        // Resolution and the preview spec are derived from the frozen template
        // table, so the field can never drift away from the preset.
        if (PresetBox is null || DpiBox is null || Preview is null)
        {
            return;
        }

        DpiBox.SelectedIndex = PresetBox.SelectedIndex switch
        {
            1 => 1,
            2 => 2,
            _ => 0,
        };

        IdPhotoPreset? preset = CurrentPreset();
        if (preset is not null)
        {
            Preview.PreviewSpec = $"{preset.OutputWidthPx} × {preset.OutputHeightPx} · {preset.Dpi} DPI · 未处理";
        }
    }

    private IdPhotoPreset? CurrentPreset()
    {
        if ((PresetBox.SelectedItem as ComboBoxItem)?.Tag is not string presetId)
        {
            return null;
        }

        foreach (IdPhotoPreset preset in IdPhotoTemplates.All)
        {
            if (string.Equals(preset.PresetId, presetId, StringComparison.Ordinal))
            {
                return preset;
            }
        }

        return null;
    }

    public async Task AddFilesAsync()
    {
        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.Thumbnail,
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
        };

        foreach (string extension in ImageExtensions)
        {
            picker.FileTypeFilter.Add(extension);
        }

        if (MainWindow.Current is not null)
        {
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(MainWindow.Current));
        }

        IReadOnlyList<Windows.Storage.StorageFile> files = await picker.PickMultipleFilesAsync();
        foreach (Windows.Storage.StorageFile file in files)
        {
            if (!_inputPaths.Contains(file.Path))
            {
                _inputPaths.Add(file.Path);
            }
        }

        UpdateFileState();
    }

    private void UpdateFileState()
    {
        if (_inputPaths.Count == 0)
        {
            Preview.PreviewName = "尚未添加文件";
            Preview.PreviewSpec = "支持 JPG / PNG 人像照片";
            LeftFilePanelControl.Current?.ShowFile(null, null, null);
            return;
        }

        string first = _inputPaths[0];
        string name = Path.GetFileName(first);
        long bytes = new FileInfo(first).Length;

        Preview.PreviewName = _inputPaths.Count == 1 ? name : $"{name} 等 {_inputPaths.Count} 个文件";
        OutputPathText.Text = Path.GetDirectoryName(first) ?? "与原文件相同";
        LeftFilePanelControl.Current?.ShowFile(name, $"{FormatSize(bytes)}\n图像文件", "等待处理");
        SyncSpecRows();
    }

    private void OnRatioChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        int value = (int)Math.Round(e.NewValue);
        RatioValueText.Text = value switch
        {
            >= 70 and <= 74 => $"合格 · {value}%",
            >= 65 and <= 78 => $"可接受 · {value}%",
            _ => $"需调整 · {value}%",
        };
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        PresetBox.SelectedIndex = 0;
        BackgroundBox.SelectedIndex = 0;
        RatioSlider.Value = 68;
        HardwareSwitch.IsOn = true;
        StatusBar.IsOpen = false;
        SyncSpecRows();
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_submission is null)
        {
            return;
        }

        if (_inputPaths.Count == 0)
        {
            SetStatus("请先添加至少一张人像照片。", InfoBarSeverity.Warning);
            return;
        }

        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["preset"] = (PresetBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "office-1in",
        };

        if (_toolId == "idphoto.create")
        {
            parameters["background"] = (BackgroundBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "蓝";
        }
        else if (_toolId == "idphoto.print")
        {
            parameters["paper"] = "6inch";
        }

        JobRequest request = JobRequest.Create(
            _toolId,
            _inputPaths,
            Path.GetDirectoryName(_inputPaths[0])!,
            JobWorkload.HeavyMedia,
            parameters);

        string action = _toolId switch
        {
            "idphoto.check" => "规格检查",
            "idphoto.print" => "排版输出",
            _ => "生成证件照",
        };

        SetStatus("正在提交任务…", InfoBarSeverity.Informational);
        TaskQueueControl.Current?.ShowTask(Path.GetFileName(_inputPaths[0]), $"{action} · 正在处理", 0);

        JobRecord record = await _submission.SubmitAsync(request);
        TaskQueueControl.Current?.ShowIdle();

        if (record.Status == JobStatus.Succeeded)
        {
            SetStatus($"完成：{record.OutputPaths.Count} 个文件已输出。", InfoBarSeverity.Success);
        }
        else if (record.Error is not null)
        {
            SetStatus($"未能完成：{record.Error.Message}", InfoBarSeverity.Error);
        }
        else
        {
            SetStatus(record.Status.ToString(), InfoBarSeverity.Warning);
        }
    }

    private void SetStatus(string message, InfoBarSeverity severity)
    {
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024 / 1024:F2} GB",
        >= 1024L * 1024 => $"{bytes / 1024.0 / 1024:F1} MB",
        >= 1024 => $"{bytes / 1024.0:F0} KB",
        _ => $"{bytes} B",
    };
}
