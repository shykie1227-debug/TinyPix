using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using TinyPix.App.Services;
using TinyPix.App.ViewModels;
using TinyPix.Core.Jobs;
using TinyPix.Core.Tools;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace TinyPix.App.Pages;

/// <summary>
/// 视频编辑合并入口页 (Phase 3). Routes the selected tool id to its registered
/// handler through JobSubmissionService, using a conservative default parameter
/// set so the FFmpeg pipeline can be validated on the exe machine first.
/// </summary>
public sealed partial class VideoEditorPage : Page
{
    private readonly List<string> _inputPaths = new();
    private JobSubmissionService? _submission;

    public VideoEditorPage()
    {
        InitializeComponent();
    }

    public MainWindowViewModel? ViewModel { get; private set; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel = e.Parameter as MainWindowViewModel;
        _submission = App.Services.GetRequiredService<JobSubmissionService>();
        SetStatus("尚未添加文件。");
    }

    private async void AddFilesButton_Click(object sender, RoutedEventArgs e)
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

        SetStatus(_inputPaths.Count == 0
            ? "尚未添加文件。"
            : $"已添加 {_inputPaths.Count} 个文件。");
    }

    private async void StartEditButton_Click(object sender, RoutedEventArgs e)
    {
        if (_submission is null)
        {
            return;
        }

        if (_inputPaths.Count == 0)
        {
            SetStatus("请先添加至少一个视频文件。");
            return;
        }

        string? toolId = (ToolBox.SelectedItem as ComboBoxItem)?.Tag as string;
        if (string.IsNullOrEmpty(toolId))
        {
            SetStatus("请选择一个工具。");
            return;
        }

        var parameters = DefaultParametersFor(toolId);
        string outputDirectory = Path.GetDirectoryName(_inputPaths[0])!;

        SetStatus("正在提交任务…");
        try
        {
            JobRequest request = JobRequest.Create(
                toolId,
                _inputPaths,
                outputDirectory,
                JobWorkload.HeavyMedia,
                parameters);

            JobRecord record = await _submission.SubmitAsync(request);
            SetStatus(record.Status == JobStatus.Succeeded
                ? "任务已完成。"
                : $"任务状态：{record.Status} {record.Error?.Message ?? string.Empty}");
        }
        catch (Exception ex)
        {
            SetStatus($"提交失败：{ex.Message}");
        }
    }

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

    private void SetStatus(string message) => StatusText.Text = message;
}
