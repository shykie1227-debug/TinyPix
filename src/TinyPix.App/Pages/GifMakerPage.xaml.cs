using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
/// GIF 制作 workbench page (video.gif). Phase 2 wires the page to the GifMakerHandler
/// through JobSubmissionService. Because the tool is supportsBatch=false with a
/// per-file clip interval, each selected file is enqueued as its own job.
/// </summary>
public sealed partial class GifMakerPage : Page
{
    private readonly List<string> _inputPaths = new();
    private JobSubmissionService? _submission;

    public GifMakerPage()
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

        int fps = MapFps((FpsBox.SelectedItem as ComboBoxItem)?.Content?.ToString());
        int width = MapWidth((SizeBox.SelectedItem as ComboBoxItem)?.Content?.ToString());
        int loopCount = MapLoop((LoopBox.SelectedItem as ComboBoxItem)?.Content?.ToString());
        double startSecs = StartTimeBox.Value;
        double endSecs = EndTimeBox.Value;
        int quality = (int)Math.Round(QualitySlider.Value);

        SetStatus("正在提交任务…");
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

        SetStatus(succeeded == _inputPaths.Count
            ? $"完成：{succeeded} 个 GIF 已生成。"
            : $"已完成 {succeeded}/{_inputPaths.Count} 个；{lastError}");
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

    private void SetStatus(string message) => StatusText.Text = message;
}
