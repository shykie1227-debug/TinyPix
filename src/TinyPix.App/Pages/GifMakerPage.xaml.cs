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
/// Key Page / GIF 制作 — design frame SeIGS, wired to GifMakerHandler through
/// JobSubmissionService. supportsBatch=false with a per-file clip interval, so
/// each selected file is enqueued as its own job.
/// </summary>
public sealed partial class GifMakerPage : Page, IWorkbenchFilePicker
{
    private readonly List<string> _inputPaths = new();
    private JobSubmissionService? _submission;

    public GifMakerPage()
    {
        InitializeComponent();
        Presets.SetPresets(new[]
        {
            new PresetCard("\uE73E", "聊天表情", "480 px · 12 FPS", true),
            new PresetCard("\uE7B8", "演示动图", "720 px · 15 FPS"),
            new PresetCard("\uE774", "高质量", "原尺寸 · 20 FPS"),
            new PresetCard("\uE710", "自定义预设", "保存当前参数"),
        });
    }

    public MainWindowViewModel? ViewModel { get; private set; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel = e.Parameter is WorkbenchNavigation navigation
            ? navigation.ViewModel
            : e.Parameter as MainWindowViewModel;
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
            Preview.PreviewSpec = "GIF 最长 30 秒";
            LeftFilePanelControl.Current?.ShowFile(null, null, null);
            return;
        }

        string first = _inputPaths[0];
        string name = Path.GetFileName(first);
        long bytes = new FileInfo(first).Length;

        Preview.PreviewName = _inputPaths.Count == 1 ? name : $"{name} 等 {_inputPaths.Count} 个文件";
        Preview.PreviewSpec = $"{FormatSize(bytes)} · 将转为 GIF";
        LeftFilePanelControl.Current?.ShowFile(name, $"{FormatSize(bytes)}\nGIF", "等待处理");
    }

    private void OnQualityChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        int value = (int)Math.Round(e.NewValue);
        QualityValueText.Text = value switch
        {
            >= 85 => "极高 · 抖动优化",
            >= 65 => "高 · 调色板优化",
            >= 40 => "标准 · 平衡体积",
            _ => "紧凑 · 小体积",
        };
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        StartTimeBox.Value = 0;
        EndTimeBox.Value = 5;
        SizeBox.SelectedIndex = 1;
        FpsBox.SelectedIndex = 0;
        LoopBox.SelectedIndex = 0;
        QualitySlider.Value = 80;
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

        int fps = MapFps((FpsBox.SelectedItem as ComboBoxItem)?.Content?.ToString());
        int width = MapWidth((SizeBox.SelectedItem as ComboBoxItem)?.Content?.ToString());
        int loopCount = MapLoop((LoopBox.SelectedItem as ComboBoxItem)?.Content?.ToString());
        double startSecs = double.IsNaN(StartTimeBox.Value) ? 0 : StartTimeBox.Value;
        double endSecs = double.IsNaN(EndTimeBox.Value) ? 5 : EndTimeBox.Value;
        int quality = (int)Math.Round(QualitySlider.Value);

        SetStatus("正在提交任务…", InfoBarSeverity.Informational);
        int succeeded = 0;
        var lastError = string.Empty;
        foreach (string input in _inputPaths)
        {
            string outputDirectory = Path.GetDirectoryName(input)!;
            var parameters = new Dictionary<string, string>
            {
                ["format"] = "gif",
                ["startSecs"] = startSecs.ToString(CultureInfo.InvariantCulture),
                ["endSecs"] = endSecs.ToString(CultureInfo.InvariantCulture),
                ["fps"] = fps.ToString(CultureInfo.InvariantCulture),
                ["width"] = width.ToString(CultureInfo.InvariantCulture),
                ["loopCount"] = loopCount.ToString(CultureInfo.InvariantCulture),
                ["quality"] = quality.ToString(CultureInfo.InvariantCulture),
            };

            JobRequest request = JobRequest.Create(
                "video.gif",
                new[] { input },
                outputDirectory,
                JobWorkload.HeavyMedia,
                parameters);

            TaskQueueControl.Current?.ShowTask(Path.GetFileName(input), $"GIF · {fps} FPS", 0);
            JobRecord record = await _submission.SubmitAsync(request);
            if (record.Status == JobStatus.Succeeded)
            {
                succeeded++;
            }
            else if (record.Error is not null)
            {
                lastError = record.Error.Message;
            }
        }

        TaskQueueControl.Current?.ShowIdle();
        SetStatus(
            succeeded == _inputPaths.Count
                ? $"完成：{succeeded} 个 GIF 已生成。"
                : $"已完成 {succeeded}/{_inputPaths.Count} 个；{lastError}",
            succeeded == _inputPaths.Count ? InfoBarSeverity.Success : InfoBarSeverity.Error);
    }

    private static int MapFps(string? display) =>
        int.TryParse(display, NumberStyles.Integer, CultureInfo.InvariantCulture, out int fps) && fps > 0 ? fps : 12;

    private static int MapWidth(string? display) => display switch
    {
        "320P" => 320,
        "480P" => 480,
        "640P" => 640,
        "720P" => 720,
        _ => 0, // 原始：保持源分辨率
    };

    private static int MapLoop(string? display) => display switch
    {
        "1 次" => 1,
        "3 次" => 3,
        _ => 0, // 无限循环
    };

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024 / 1024:F2} GB",
        >= 1024L * 1024 => $"{bytes / 1024.0 / 1024:F1} MB",
        >= 1024 => $"{bytes / 1024.0:F0} KB",
        _ => $"{bytes} B",
    };

    private void SetStatus(string message, InfoBarSeverity severity)
    {
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }
}
