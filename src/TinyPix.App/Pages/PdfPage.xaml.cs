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
/// Template 5 / PDF 页面管理 — design frame 2UdX9. Center preview + 页面操作
/// presets, right PDF 输出 panel driving the eight pdf.* handlers.
/// </summary>
public sealed partial class PdfPage : Page, IWorkbenchFilePicker
{
    private static readonly string[] PdfExtensions = { ".pdf" };
    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".tif", ".tiff" };

    private readonly List<string> _inputPaths = new();
    private JobSubmissionService? _submission;
    private string _toolId = "pdf.merge";

    public PdfPage()
    {
        InitializeComponent();
        Presets.SetPresets(new[]
        {
            new PresetCard("\uE8C8", "合并", "多个 PDF", true),
            new PresetCard("\uE7C3", "拆分", "按页或范围"),
            new PresetCard("\uE91B", "图片转 PDF", "自动适配页面"),
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
        for (int i = 0; i < ActionBox.Items.Count; i++)
        {
            if (ActionBox.Items[i] is ComboBoxItem item
                && string.Equals(item.Tag as string, toolId, StringComparison.Ordinal))
            {
                ActionBox.SelectedIndex = i;
                return;
            }
        }
    }

    private void OnPresetInvoked(object? sender, int index)
    {
        SelectTool(index switch
        {
            0 => "pdf.merge",
            1 => "pdf.split",
            2 => "pdf.from-image",
            _ => _toolId,
        });
    }

    private void OnActionChanged(object sender, SelectionChangedEventArgs e)
    {
        if ((ActionBox.SelectedItem as ComboBoxItem)?.Tag is string toolId)
        {
            _toolId = toolId;
        }
    }

    public async Task AddFilesAsync()
    {
        bool wantsImages = string.Equals(_toolId, "pdf.from-image", StringComparison.Ordinal);

        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = wantsImages ? PickerLocationId.PicturesLibrary : PickerLocationId.DocumentsLibrary,
        };

        foreach (string extension in wantsImages ? ImageExtensions : PdfExtensions)
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
            Preview.PreviewSpec = "支持 PDF 与常见图片格式";
            LeftFilePanelControl.Current?.ShowFile(null, null, null);
            return;
        }

        string first = _inputPaths[0];
        string name = Path.GetFileName(first);
        long bytes = new FileInfo(first).Length;

        Preview.PreviewName = _inputPaths.Count == 1 ? name : $"{name} 等 {_inputPaths.Count} 个文件";
        Preview.PreviewSpec = $"{FormatSize(bytes)} · PDF 文档";
        OutputPathText.Text = Path.GetDirectoryName(first) ?? "与原文件相同";
        LeftFilePanelControl.Current?.ShowFile(name, $"{FormatSize(bytes)}\nPDF 文档", "等待处理");
    }

    private void OnQualityChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        QualityValueText.Text = (int)Math.Round(e.NewValue) switch
        {
            >= 90 => "仅清理冗余对象",
            >= 70 => "仅重采样图片",
            >= 40 => "重采样并重编码",
            _ => "强制栅格化",
        };
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        ActionBox.SelectedIndex = 0;
        PagesBox.SelectedIndex = 0;
        DpiBox.SelectedIndex = 0;
        QualitySlider.Value = 72;
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

        string pages = (PagesBox.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty;
        string dpi = (DpiBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "150";

        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        switch (_toolId)
        {
            case "pdf.split":
                parameters["pages"] = string.IsNullOrEmpty(pages) ? "1-1" : pages;
                break;
            case "pdf.reorder":
                parameters["order"] = BuildReorderSpec(_inputPaths.Count);
                break;
            case "pdf.rotate":
                parameters["angle"] = "90";
                parameters["scope"] = string.IsNullOrEmpty(pages) ? "all" : "pages";
                parameters["pages"] = pages;
                break;
            case "pdf.compress":
                parameters["confirmLossy"] = "true";
                break;
            case "pdf.to-image":
                parameters["dpi"] = dpi;
                parameters["pages"] = pages;
                break;
            case "pdf.watermark":
                parameters["text"] = "TinyPix";
                break;
        }

        if (!string.IsNullOrEmpty(pages)
            && parameters.ContainsKey("pages") == false
            && _toolId is "pdf.merge" or "pdf.compress" or "pdf.from-image")
        {
            parameters["pages"] = pages;
        }

        JobRequest request = JobRequest.Create(
            _toolId,
            _inputPaths,
            Path.GetDirectoryName(_inputPaths[0])!,
            JobWorkload.HeavyMedia,
            parameters);

        SetStatus("正在提交任务…", InfoBarSeverity.Informational);
        TaskQueueControl.Current?.ShowTask(
            Path.GetFileName(_inputPaths[0]),
            $"{DisplayName(_toolId)} · 正在处理",
            0);

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

    private static string BuildReorderSpec(int count)
    {
        int pages = Math.Max(1, count);
        var parts = new List<string>(pages);
        for (int i = pages; i >= 1; i--)
        {
            parts.Add(i.ToString(CultureInfo.InvariantCulture));
        }

        return string.Join(",", parts);
    }

    private static string DisplayName(string toolId) => toolId switch
    {
        "pdf.merge" => "PDF 合并",
        "pdf.split" => "PDF 拆分",
        "pdf.reorder" => "页面排序",
        "pdf.rotate" => "页面旋转",
        "pdf.watermark" => "PDF 水印",
        "pdf.to-image" => "PDF 转图片",
        "pdf.from-image" => "图片转 PDF",
        "pdf.compress" => "PDF 压缩",
        _ => toolId,
    };

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
