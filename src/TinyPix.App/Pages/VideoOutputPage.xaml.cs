using System;
using System.Collections.Generic;
using System.Globalization;
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
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace TinyPix.App.Pages;

/// <summary>
/// Template 1 / 视频输出 — design frame rUrqE. Center preview + 常用预设, right
/// 输出设置 panel wired to the video.convert handler through JobSubmissionService.
/// </summary>
public sealed partial class VideoOutputPage : Page, IWorkbenchFilePicker
{
    private readonly List<string> _inputPaths = new();
    private JobSubmissionService? _submission;

    public VideoOutputPage()
    {
        InitializeComponent();
        Presets.SetPresets(new[]
        {
            new PresetCard("\uE73E", "原画质 MP4", "H.264 · 保持尺寸", true),
            new PresetCard("\uE7B8", "高效压缩", "HEVC · 节省空间"),
            new PresetCard("\uE774", "网页兼容", "WebM · 平衡质量"),
            new PresetCard("\uE710", "自定义预设", "保存当前参数"),
        });
    }

    public MainWindowViewModel? ViewModel { get; private set; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is WorkbenchNavigation navigation)
        {
            ViewModel = navigation.ViewModel;
        }
        else
        {
            ViewModel = e.Parameter as MainWindowViewModel;
        }

        _submission = App.Services.GetRequiredService<JobSubmissionService>();
    }

    public async Task AddFilesAsync()
    {
        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.Thumbnail,
            SuggestedStartLocation = PickerLocationId.VideosLibrary,
        };
        picker.FileTypeFilter.Add(".mp4");
        picker.FileTypeFilter.Add(".mov");
        picker.FileTypeFilter.Add(".mkv");
        picker.FileTypeFilter.Add(".avi");
        picker.FileTypeFilter.Add(".webm");
        picker.FileTypeFilter.Add(".m4v");
        picker.FileTypeFilter.Add(".ts");

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
            Preview.PreviewSpec = "支持 MP4 / MOV / MKV / WebM";
            LeftFilePanelControl.Current?.ShowFile(null, null, null);
            return;
        }

        string first = _inputPaths[0];
        string name = Path.GetFileName(first);
        long bytes = new FileInfo(first).Length;

        Preview.PreviewName = _inputPaths.Count == 1 ? name : $"{name} 等 {_inputPaths.Count} 个文件";
        Preview.PreviewSpec = $"{FormatSize(bytes)} · {Path.GetExtension(first).TrimStart('.').ToUpperInvariant()}";
        OutputPathText.Text = Path.GetDirectoryName(first) ?? "与原文件相同";
        LeftFilePanelControl.Current?.ShowFile(
            name,
            $"{FormatSize(bytes)}\n{Path.GetExtension(first).TrimStart('.').ToUpperInvariant()}",
            "等待处理");
    }

    private void OnQualityChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        int value = (int)Math.Round(e.NewValue);
        QualityValueText.Text = value switch
        {
            >= 90 => $"极高 · CRF 16",
            >= 75 => $"高 · CRF 18",
            >= 50 => $"标准 · CRF 23",
            >= 25 => $"紧凑 · CRF 28",
            _ => $"最小体积 · CRF 32",
        };
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        FormatBox.SelectedIndex = 0;
        CodecBox.SelectedIndex = 0;
        ResolutionBox.SelectedIndex = 0;
        QualitySlider.Value = 82;
        HardwareSwitch.IsOn = true;
        StatusBar.IsOpen = false;
    }

    private async void StartOutputButton_Click(object sender, RoutedEventArgs e)
    {
        if (_submission is null)
        {
            return;
        }

        if (_inputPaths.Count == 0)
        {
            SetStatus("请先添加至少一个视频文件。", InfoBarSeverity.Warning);
            return;
        }

        string? selected = (FormatBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
        string format = MapFormat(selected);
        int quality = (int)Math.Round(QualitySlider.Value);
        string outputDirectory = Path.GetDirectoryName(_inputPaths[0])!;

        var parameters = new Dictionary<string, string>
        {
            ["format"] = format,
            ["quality"] = quality.ToString(CultureInfo.InvariantCulture),
        };

        JobRequest request = JobRequest.Create(
            "video.convert",
            _inputPaths,
            outputDirectory,
            JobWorkload.HeavyMedia,
            parameters);

        SetStatus("正在提交任务…", InfoBarSeverity.Informational);
        TaskQueueControl.Current?.ShowTask(Path.GetFileName(_inputPaths[0]), $"{format.ToUpperInvariant()} · 正在输出", 0);

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

    private static string MapFormat(string? display) => display switch
    {
        "MOV" => "mov",
        "MKV" => "mkv",
        "AVI" => "avi",
        "WebM" => "webm",
        "MP3（仅音频）" => "mp3",
        "WAV（仅音频）" => "wav",
        "AAC（仅音频）" => "aac",
        "FLAC（仅音频）" => "flac",
        _ => "mp4",
    };
}
