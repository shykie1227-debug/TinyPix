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
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace TinyPix.App.Pages;

/// <summary>
/// Template 7 / OCR 与二维码识别 — design frame NIPNW. Center preview + 识别模式
/// presets, right 识别设置 panel. QR/barcode decoding runs through code.scan;
/// the two OCR entries are surfaced honestly because no OCR engine ships yet.
/// </summary>
public sealed partial class OcrQrPage : Page, IWorkbenchFilePicker
{
    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".gif", ".tif", ".tiff" };

    private readonly List<string> _inputPaths = new();
    private JobSubmissionService? _submission;
    private string _toolId = "code.scan";

    public OcrQrPage()
    {
        InitializeComponent();
        Presets.SetPresets(new[]
        {
            new PresetCard("\uE91B", "图片 OCR", "本地识别"),
            new PresetCard("\uE7C3", "扫描 PDF", "逐页识别"),
            new PresetCard("\uE721", "二维码", "显示文本与类型", true),
            new PresetCard("\uE710", "自定义预设", "保存当前参数"),
        });

        Presets.PresetInvoked += OnPresetInvoked;
    }

    public MainWindowViewModel? ViewModel { get; private set; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is WorkbenchNavigation navigation)
        {
            ViewModel = navigation.ViewModel;
            if (!string.IsNullOrEmpty(navigation.ToolId))
            {
                SelectTool(navigation.ToolId);
            }
        }
        else
        {
            ViewModel = e.Parameter as MainWindowViewModel;
        }

        _submission = App.Services.GetRequiredService<JobSubmissionService>();
    }

    private void SelectTool(string toolId)
    {
        _toolId = toolId;
        SyncPresetSelection(toolId);

        for (int i = 0; i < KindBox.Items.Count; i++)
        {
            if (KindBox.Items[i] is ComboBoxItem item
                && string.Equals(item.Tag as string, toolId, StringComparison.Ordinal))
            {
                KindBox.SelectedIndex = i;
                return;
            }
        }
    }

    /// <summary>
    /// Keeps the highlighted preset card in step with the recognition kind, so
    /// entering from the left tool list (e.g. idphoto-style ocr.image) does not
    /// leave the QR card selected.
    /// </summary>
    private void SyncPresetSelection(string toolId) =>
        Presets.Select(toolId switch
        {
            "ocr.image" => 0,
            "ocr.pdf" => 1,
            "code.scan" => 2,
            _ => -1,
        });

    private void OnPresetInvoked(object? sender, int index)
    {
        SelectTool(index switch
        {
            0 => "ocr.image",
            1 => "ocr.pdf",
            2 => "code.scan",
            _ => _toolId,
        });
    }

    private void OnKindChanged(object sender, SelectionChangedEventArgs e)
    {
        if ((KindBox.SelectedItem as ComboBoxItem)?.Tag is string toolId)
        {
            _toolId = toolId;
        }
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

        picker.FileTypeFilter.Add(".pdf");

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
            Preview.PreviewSpec = "支持 PNG / JPG / WebP / PDF";
            LeftFilePanelControl.Current?.ShowFile(null, null, null);
            return;
        }

        string first = _inputPaths[0];
        string name = Path.GetFileName(first);
        long bytes = new FileInfo(first).Length;
        string kind = string.Equals(Path.GetExtension(first), ".pdf", StringComparison.OrdinalIgnoreCase)
            ? "PDF 文档"
            : "图像文件";

        Preview.PreviewName = _inputPaths.Count == 1 ? name : $"{name} 等 {_inputPaths.Count} 个文件";
        Preview.PreviewSpec = $"{FormatSize(bytes)} · {kind}";
        OutputPathText.Text = Path.GetDirectoryName(first) ?? "与原文件相同";
        LeftFilePanelControl.Current?.ShowFile(name, $"{FormatSize(bytes)}\n{kind}", "等待处理");
    }

    private void OnLayoutChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        LayoutValueText.Text = (int)Math.Round(e.NewValue) switch
        {
            >= 80 => "严格保留版面",
            >= 45 => "保留段落顺序",
            _ => "纯文本流",
        };
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        KindBox.SelectedIndex = 0;
        LanguageBox.SelectedIndex = 0;
        PagesBox.SelectedIndex = 0;
        LayoutSlider.Value = 74;
        HardwareSwitch.IsOn = true;
        StatusBar.IsOpen = false;
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_submission is null)
        {
            return;
        }

        if (_inputPaths.Count == 0)
        {
            SetStatus("请先添加至少一个文件。", InfoBarSeverity.Warning);
            return;
        }

        if (_toolId is "ocr.image" or "ocr.pdf")
        {
            // No Tesseract engine is bundled in this build; say so instead of
            // queueing a job that can only fail inside the pipeline.
            SetStatus(
                "当前版本未随包提供 OCR 文字识别引擎，请先使用「二维码 / 条码」识别。",
                InfoBarSeverity.Warning);
            return;
        }

        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["export"] = "true",
        };

        JobRequest request = JobRequest.Create(
            _toolId,
            _inputPaths,
            Path.GetDirectoryName(_inputPaths[0])!,
            JobWorkload.HeavyMedia,
            parameters);

        SetStatus("正在提交任务…", InfoBarSeverity.Informational);
        TaskQueueControl.Current?.ShowTask(Path.GetFileName(_inputPaths[0]), "二维码识别 · 正在扫描", 0);

        JobRecord record = await _submission.SubmitAsync(request);
        TaskQueueControl.Current?.ShowIdle();

        if (record.Status == JobStatus.Succeeded)
        {
            SetStatus($"完成：识别结果已输出到 {record.OutputPaths.Count} 个文件。", InfoBarSeverity.Success);
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
