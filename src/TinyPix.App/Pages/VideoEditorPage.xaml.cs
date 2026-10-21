using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
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
using TinyPix.Media.Ffmpeg;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace TinyPix.App.Pages;

/// <summary>
/// Template 3 / 视频时间剪辑 — design frame fj5RE. Owns the non-destructive
/// single-track timeline and routes the nine video.* tools through
/// JobSubmissionService, with the timeline driving trim/split parameters.
/// </summary>
public sealed partial class VideoEditorPage : Page, IWorkbenchFilePicker
{
    private readonly List<string> _inputPaths = new();
    private JobSubmissionService? _submission;
    private IFfmpegEngineProvider? _engine;
    private string _toolId = "video.trim";

    public VideoEditorPage()
    {
        InitializeComponent();
        Timeline.StateChanged += OnTimelineStateChanged;
    }

    public MainWindowViewModel? ViewModel { get; private set; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is WorkbenchNavigation navigation)
        {
            ViewModel = navigation.ViewModel;
            if (navigation.ToolId is { Length: > 0 } toolId)
            {
                SelectTool(toolId);
            }
        }
        else
        {
            ViewModel = e.Parameter as MainWindowViewModel;
        }

        _submission = App.Services.GetRequiredService<JobSubmissionService>();
        _engine = App.Services.GetService<IFfmpegEngineProvider>();
    }

    private void SelectTool(string toolId)
    {
        _toolId = toolId;
        for (int i = 0; i < ModeBox.Items.Count; i++)
        {
            if (ModeBox.Items[i] is ComboBoxItem item
                && string.Equals(item.Tag as string, toolId, StringComparison.Ordinal))
            {
                ModeBox.SelectedIndex = i;
                return;
            }
        }
    }

    private void OnModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if ((ModeBox.SelectedItem as ComboBoxItem)?.Tag is string toolId)
        {
            _toolId = toolId;
            StatusBar.IsOpen = false;
        }
    }

    private void OnRangeChanged(object sender, SelectionChangedEventArgs e) => StatusBar.IsOpen = false;

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

        await UpdateFileStateAsync();
    }

    private async Task UpdateFileStateAsync()
    {
        if (_inputPaths.Count == 0)
        {
            Preview.PreviewName = "尚未添加文件";
            Preview.PreviewSpec = "支持 MP4 / MOV / MKV / WebM";
            Timeline.Load(0);
            LeftFilePanelControl.Current?.ShowFile(null, null, null);
            return;
        }

        string first = _inputPaths[0];
        string name = Path.GetFileName(first);
        long bytes = new FileInfo(first).Length;
        double duration = await ProbeDurationAsync(first);

        Preview.PreviewName = _inputPaths.Count == 1 ? name : $"{name} 等 {_inputPaths.Count} 个文件";
        Preview.PreviewSpec = $"{Path.GetExtension(first).TrimStart('.').ToUpperInvariant()} · {FormatSize(bytes)} · {Clock(duration)}";
        OutputPathText.Text = Path.GetDirectoryName(first) ?? "与原文件相同";

        Timeline.Load(duration);
        Preview.SetTimeRange("00:00", Clock(duration));

        LeftFilePanelControl.Current?.ShowFile(
            name,
            $"{FormatSize(bytes)}\n{Clock(duration)}",
            "等待处理");
    }

    private async Task<double> ProbeDurationAsync(string path)
    {
        if (_engine is null)
        {
            return 0;
        }

        try
        {
            var runner = new FfprobeRunner();
            FfprobeResult result = await runner.RunAsync(_engine.FfprobeExecutablePath, path);
            if (result.ExitCode != 0)
            {
                return 0;
            }

            using JsonDocument document = JsonDocument.Parse(result.StandardOutput);
            if (document.RootElement.TryGetProperty("format", out JsonElement format)
                && format.TryGetProperty("duration", out JsonElement duration)
                && double.TryParse(duration.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
            {
                return seconds;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ffprobe failed for {path}: {ex.Message}");
        }

        return 0;
    }

    private void OnTimelineStateChanged(object? sender, EventArgs e)
    {
        Preview.SetTimeRange(Clock(Timeline.InSeconds), Clock(Timeline.OutSeconds));
        StatusBar.IsOpen = false;
    }

    private void OnVolumeChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        int value = (int)Math.Round(e.NewValue);
        AudioValueText.Text = value switch
        {
            0 => "静音",
            100 => "保留原音轨",
            > 100 => $"增强至 {value}%",
            _ => $"降低至 {value}%",
        };
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        ModeBox.SelectedIndex = 0;
        PrecisionBox.SelectedIndex = 0;
        RangeBox.SelectedIndex = 0;
        VolumeSlider.Value = 100;
        HardwareSwitch.IsOn = true;
        StatusBar.IsOpen = false;
        _ = UpdateFileStateAsync();
    }

    private async void StartEditButton_Click(object sender, RoutedEventArgs e)
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

        Dictionary<string, string> parameters = DefaultParametersFor(_toolId);
        string range = (RangeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "inout";

        if (_toolId == "video.trim")
        {
            double start = Timeline.InSeconds;
            double end = Timeline.OutSeconds;
            if (range == "all" || end <= start)
            {
                start = 0;
                end = Timeline.Duration;
            }

            parameters["startSecs"] = Num(start);
            parameters["endSecs"] = Num(end);
            parameters["precise"] = (PrecisionBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "0";
        }
        else if (_toolId == "video.split")
        {
            parameters["points"] = string.Create(
                CultureInfo.InvariantCulture,
                $"{Num(Timeline.InSeconds)},{Num(Timeline.OutSeconds)}");
        }
        else if (_toolId == "video.speed-volume")
        {
            parameters["volume"] = ((int)Math.Round(VolumeSlider.Value)).ToString(CultureInfo.InvariantCulture);
        }

        JobRequest request = JobRequest.Create(
            _toolId,
            _inputPaths,
            Path.GetDirectoryName(_inputPaths[0])!,
            JobWorkload.HeavyMedia,
            parameters);

        SetStatus("正在提交任务…", InfoBarSeverity.Informational);
        TaskQueueControl.Current?.ShowTask(Path.GetFileName(_inputPaths[0]), $"{ModeText()} · 正在处理", 0);

        try
        {
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
        catch (Exception ex)
        {
            TaskQueueControl.Current?.ShowIdle();
            SetStatus($"提交失败：{ex.Message}", InfoBarSeverity.Error);
        }
    }

    private string ModeText() => (ModeBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "视频剪辑";

    private static string Num(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Clock(double seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)span.TotalMinutes:00}:{span.Seconds:00}.{span.Milliseconds:000}";
    }

    private void SetStatus(string message, InfoBarSeverity severity)    {
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

    private static Dictionary<string, string> DefaultParametersFor(string toolId) => toolId switch
    {
        "video.trim" => new() { ["startSecs"] = "0", ["endSecs"] = "5", ["precise"] = "0" },
        "video.split" => new() { ["points"] = "2.5,5" },
        "video.merge" => new(),
        "video.geometry" => new() { ["scaleW"] = "0", ["rotate"] = "0", ["flipH"] = "0", ["flipV"] = "0" },
        "video.speed-volume" => new() { ["speed"] = "1", ["volume"] = "100" },
        "video.audio" => new() { ["mode"] = "extract" },
        "video.overlay" => new() { ["mode"] = "watermark" },
        "video.frames" => new() { ["mode"] = "sequence", ["count"] = "10", ["intervalSecs"] = "1" },
        "video.metadata" => new() { ["mode"] = "clean" },
        _ => new(),
    };
}
