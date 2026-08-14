using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TinyPix.App.Services;
using TinyPix.App.ViewModels;
using TinyPix.Core.Jobs;
using TinyPix.Core.Tools;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace TinyPix.App.Pages;

/// <summary>
/// 视频输出 workbench page. Receives the shell MainWindowViewModel through Frame
/// navigation so page state and the shared task queue stay consistent. Phase 1
/// wires the page to the video.convert handler via JobSubmissionService.
/// </summary>
public sealed partial class VideoOutputPage : Page
{
    private readonly List<string> _inputPaths = new();
    private JobSubmissionService? _submission;

    public VideoOutputPage()
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

        if (MainWindow is not null)
        {
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(MainWindow));
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

    private async void StartOutputButton_Click(object sender, RoutedEventArgs e)
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

        string? selected = (FormatBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
        string format = MapFormat(selected);
        int quality = (int)Math.Round(QualitySlider.Value);
        string outputDirectory = Path.GetDirectoryName(_inputPaths[0])!;

        var parameters = new Dictionary<string, string>
        {
            ["format"] = format,
            ["quality"] = quality.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        JobRequest request = JobRequest.Create(
            "video.convert",
            _inputPaths,
            outputDirectory,
            JobWorkload.HeavyMedia,
            parameters);

        SetStatus("正在提交任务…");
        JobRecord record = await _submission.SubmitAsync(request);
        SetStatus(record.Status == JobStatus.Succeeded
            ? $"完成：{record.OutputPaths.Count} 个文件已输出。"
            : record.Error is not null
                ? $"未能完成：{record.Error.Message}"
                : record.Status.ToString());
    }

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

    private void SetStatus(string message) => StatusText.Text = message;
}
