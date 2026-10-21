using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
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
/// Template 8 / 生成器与文件工具 — design frame BlxnH. One panel shape (four
/// field groups + slider + output directory) that relabels itself for
/// code.generate / file.rename / file.hash / file.metadata.
/// </summary>
public sealed partial class GeneratorsPage : Page, IWorkbenchFilePicker
{
    private readonly List<string> _inputPaths = new();
    private JobSubmissionService? _submission;
    private ToolForm _form;
    private string _toolId = "code.generate";

    /// <summary>
    /// False until InitializeComponent returns. The XAML sets
    /// TaskBox.SelectedIndex, which raises OnTaskChanged *while the parser is
    /// still building the page*, so every x:Name declared below the task combo
    /// (StartButtonText included) is still null at that point.
    /// </summary>
    private bool _ready;

    public GeneratorsPage()
    {
        InitializeComponent();
        _form = Forms["code.generate"];
        Presets.PresetInvoked += OnPresetInvoked;
        _ready = true;
        ApplyForm(_form);
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
        if (!Forms.ContainsKey(toolId))
        {
            SetStatus(
                $"「{toolId}」在当前版本尚未提供独立界面，请从下方任务类型中选择可用工具。",
                InfoBarSeverity.Warning);
            return;
        }

        _toolId = toolId;
        for (int i = 0; i < TaskBox.Items.Count; i++)
        {
            if (TaskBox.Items[i] is ComboBoxItem item
                && string.Equals(item.Tag as string, toolId, StringComparison.Ordinal))
            {
                TaskBox.SelectedIndex = i;
                break;
            }
        }

        _form = Forms[toolId];
        ApplyForm(_form);
    }

    private void OnTaskChanged(object sender, SelectionChangedEventArgs e)
    {
        if ((TaskBox.SelectedItem as ComboBoxItem)?.Tag is string toolId && Forms.TryGetValue(toolId, out ToolForm? form))
        {
            _toolId = toolId;
            _form = form;
            ApplyForm(form);
        }
    }

    private void OnPresetInvoked(object? sender, int index)
    {
        string[] order = _toolId switch
        {
            "file.rename" => new[] { "file.rename", "file.rename", "file.rename", "file.rename" },
            "file.hash" => new[] { "file.hash", "file.hash", "file.hash", "file.hash" },
            "file.metadata" => new[] { "file.metadata", "file.metadata", "file.metadata", "file.metadata" },
            _ => new[] { "code.generate", "file.rename", "file.hash", "code.generate" },
        };

        SelectTool(order[Math.Clamp(index, 0, order.Length - 1)]);

        if (_toolId == "code.generate" && index == 0)
        {
            ContentTextBox.Text = "https://tinypix.local";
        }
    }

    private void ApplyForm(ToolForm form)
    {
        // Called too early from OnTaskChanged during XAML parsing — the fields
        // further down the file are not materialised yet.
        if (!_ready)
        {
            return;
        }

        Header.Title = form.Title;
        Header.Subtitle = form.Subtitle;
        Preview.Glyph = form.Glyph;
        Preview.PreviewSpec = form.PreviewSpec;
        StartButtonText.Text = form.ActionText;
        AutomationProperties.SetName(StartButton, form.ActionText);

        Presets.SectionTitle = form.PresetTitle;
        Presets.SetPresets(form.Presets);

        Group1Label.Text = form.Label1;
        Group2Label.Text = form.Label2;
        Group3Label.Text = form.Label3;
        Fill(Group1Box, form.Options1);
        Fill(Group2Box, form.Options2);
        Fill(Group3Box, form.Options3);

        SliderLabel.Text = form.SliderLabel;
        SliderGroup.Visibility = form.ShowSlider ? Visibility.Visible : Visibility.Collapsed;
        SliderMinText.Text = form.SliderMin;
        SliderMaxText.Text = form.SliderMax;
        ValueSlider.Value = 72;
        UpdateSliderText(72);

        ContentGroup.Visibility = form.ShowContent ? Visibility.Visible : Visibility.Collapsed;
        if (form.ShowContent)
        {
            ContentLabel.Text = form.ContentLabel;
            ContentTextBox.PlaceholderText = form.ContentPlaceholder;
        }

        Group1.Visibility = form.Options1.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Group2.Visibility = form.Options2.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Group3.Visibility = form.Options3.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void Fill(ComboBox box, IReadOnlyList<FieldOption> options)
    {
        box.Items.Clear();
        foreach (FieldOption option in options)
        {
            box.Items.Add(new ComboBoxItem { Content = option.Display, Tag = option.Tag });
        }

        if (box.Items.Count > 0)
        {
            box.SelectedIndex = 0;
        }
    }

    private static string TagOf(ComboBox box) =>
        (box.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty;

    public async Task AddFilesAsync()
    {
        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };

        foreach (string extension in new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".txt", ".pdf", ".mp4", ".mov" })
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
            LeftFilePanelControl.Current?.ShowFile(null, null, null);
            return;
        }

        string first = _inputPaths[0];
        string name = Path.GetFileName(first);
        long bytes = new FileInfo(first).Length;
        string kind = Path.GetExtension(first).TrimStart('.').ToUpperInvariant();

        OutputPathText.Text = Path.GetDirectoryName(first) ?? "与原文件相同";

        if (_toolId is "code.generate")
        {
            Preview.PreviewName = "预览二维码";
            Preview.PreviewSpec = $"{TagOf(Group2Box)} × {TagOf(Group2Box)} · 错误修正 {TagOf(Group3Box)}";
        }
        else
        {
            Preview.PreviewName = _inputPaths.Count == 1 ? name : $"{name} 等 {_inputPaths.Count} 个文件";
            Preview.PreviewSpec = $"{FormatSize(bytes)} · {kind}";
        }

        LeftFilePanelControl.Current?.ShowFile(name, $"{FormatSize(bytes)}\n{kind}", "等待处理");
    }

    private void OnSliderChanged(object sender, RangeBaseValueChangedEventArgs e) =>
        UpdateSliderText((int)Math.Round(e.NewValue));

    private void UpdateSliderText(int value)
    {
        SliderValueText.Text = _toolId switch
        {
            "code.generate" => value switch
            {
                >= 80 => "SVG · 矢量",
                >= 45 => "PNG · 透明边距",
                _ => "PNG · 最小体积",
            },
            "file.rename" => value switch
            {
                >= 80 => "4 位编号",
                >= 55 => "3 位编号",
                >= 30 => "2 位编号",
                _ => "不补零",
            },
            "file.hash" => value switch
            {
                >= 80 => "导出 CSV",
                >= 45 => "导出 TXT",
                _ => "仅屏显",
            },
            _ => _form.ShowSlider ? "标准" : string.Empty,
        };
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        ApplyForm(_form);
        HardwareSwitch.IsOn = true;
        ContentTextBox.Text = string.Empty;
        StatusBar.IsOpen = false;
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_submission is null)
        {
            return;
        }

        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        bool needsInput = true;

        switch (_toolId)
        {
            case "code.generate":
                needsInput = false;
                parameters["format"] = TagOf(Group1Box) is { Length: > 0 } codeFormat ? codeFormat : "QR";
                parameters["size"] = TagOf(Group2Box);
                parameters["ecLevel"] = TagOf(Group3Box);
                parameters["output"] = (int)Math.Round(ValueSlider.Value) >= 80 ? "svg" : "png";
                parameters["content"] = string.IsNullOrWhiteSpace(ContentTextBox.Text)
                    ? "https://tinypix.local"
                    : ContentTextBox.Text.Trim();
                break;

            case "file.rename":
                parameters["numbering"] = "true";
                parameters["startNumber"] = TagOf(Group2Box);
                parameters["lockExtension"] = TagOf(Group3Box) == "keep" ? "true" : "false";
                if (TagOf(Group1Box) == "prefix")
                {
                    parameters["prefix"] = "TinyPix-";
                }
                else if (TagOf(Group1Box) == "replace")
                {
                    parameters["find"] = "_";
                    parameters["replace"] = "-";
                }

                break;

            case "file.hash":
                parameters["algorithm"] = TagOf(Group1Box);
                parameters["export"] = (int)Math.Round(ValueSlider.Value) >= 45 ? "true" : "false";
                break;

            case "file.metadata":
                parameters["mode"] = TagOf(Group1Box);
                parameters["export"] = TagOf(Group2Box) == "txt" ? "true" : "false";
                break;
        }

        if (needsInput && _inputPaths.Count == 0)
        {
            SetStatus("请先在左侧添加至少一个文件。", InfoBarSeverity.Warning);
            return;
        }

        string outputDirectory = _inputPaths.Count > 0
            ? Path.GetDirectoryName(_inputPaths[0]) ?? Environment.CurrentDirectory
            : Environment.CurrentDirectory;

        JobRequest request = JobRequest.Create(
            _toolId,
            needsInput ? (IEnumerable<string>)_inputPaths : Array.Empty<string>(),
            outputDirectory,
            JobWorkload.LightFile,
            parameters);

        string subject = needsInput && _inputPaths.Count > 0
            ? Path.GetFileName(_inputPaths[0])
            : ContentTextBox.Text.Trim();

        SetStatus("正在提交任务…", InfoBarSeverity.Informational);
        TaskQueueControl.Current?.ShowTask(subject, $"{_form.ActionText} · 正在处理", 0);

        JobRecord record = await _submission.SubmitAsync(request);
        TaskQueueControl.Current?.ShowIdle();

        if (record.Status == JobStatus.Succeeded)
        {
            SetStatus($"完成：{record.OutputPaths.Count} 个输出文件。", InfoBarSeverity.Success);
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

    // ------------------------------------------------------------------ forms

    private sealed record FieldOption(string Display, string Tag);

    private sealed record ToolForm(
        string Title,
        string Subtitle,
        string Glyph,
        string PreviewSpec,
        string PresetTitle,
        PresetCard[] Presets,
        string Label1,
        FieldOption[] Options1,
        string Label2,
        FieldOption[] Options2,
        string Label3,
        FieldOption[] Options3,
        string SliderLabel,
        bool ShowSlider,
        string SliderMin,
        string SliderMax,
        bool ShowContent,
        string ContentLabel,
        string ContentPlaceholder,
        string ActionText);

    private static readonly FieldOption[] QrSizes =
    {
        new("400 × 400 px", "400"),
        new("256 × 256 px", "256"),
        new("768 × 768 px", "768"),
        new("1024 × 1024 px", "1024"),
    };

    private static readonly FieldOption[] EcLevels =
    {
        new("M · 约 15%", "M"),
        new("L · 约 7%", "L"),
        new("Q · 约 25%", "Q"),
        new("H · 约 30%", "H"),
    };

    private static readonly Dictionary<string, ToolForm> Forms = new(StringComparer.Ordinal)
    {
        ["code.generate"] = new ToolForm(
            "二维码生成器",
            "生成本地二维码，并导出 PNG 或 SVG",
            "\uE8C8",
            "400 × 400 · 错误修正 M",
            "常用文件工具",
            new[]
            {
                new PresetCard("\uE8C8", "二维码", "纯文本内容", true),
                new PresetCard("\uE70F", "批量重命名", "预览后执行"),
                new PresetCard("\uE73E", "校验值", "SHA-256 比对"),
                new PresetCard("\uE710", "自定义预设", "保存当前参数"),
            },
            "内容类型",
            new[]
            {
                new FieldOption("纯文本", "QR"),
                new FieldOption("网址", "QR"),
                new FieldOption("名片信息", "QR"),
                new FieldOption("条码 Code 128", "Code128"),
            },
            "尺寸",
            QrSizes,
            "错误修正",
            EcLevels,
            "输出",
            true,
            "更小体积",
            "矢量优先",
            true,
            "内容",
            "输入网址或文本…",
            "生成二维码"),

        ["file.rename"] = new ToolForm(
            "批量重命名副本",
            "按模板生成改名副本，原文件保持不变",
            "\uE70F",
            "已选择文件后显示改名预览",
            "重命名规则",
            new[]
            {
                new PresetCard("\uE70F", "保留原名", "追加序号", true),
                new PresetCard("\uE8C8", "添加前缀", "TinyPix-"),
                new PresetCard("\uE721", "查找替换", "下划线转短横线"),
                new PresetCard("\uE710", "自定义预设", "保存当前参数"),
            },
            "命名模板",
            new[]
            {
                new FieldOption("保留原名 + 序号", "keep"),
                new FieldOption("添加前缀 TinyPix-", "prefix"),
                new FieldOption("下划线替换为短横线", "replace"),
            },
            "起始序号",
            new[]
            {
                new FieldOption("1", "1"),
                new FieldOption("10", "10"),
                new FieldOption("100", "100"),
            },
            "扩展名",
            new[]
            {
                new FieldOption("保持不变", "keep"),
                new FieldOption("转为小写", "lower"),
            },
            "编号补零",
            true,
            "不补零",
            "4 位编号",
            false,
            string.Empty,
            string.Empty,
            "生成改名副本"),

        ["file.hash"] = new ToolForm(
            "文件校验值",
            "计算 SHA-256 / SHA-1 / MD5 并与期望值比对",
            "\uE73E",
            "选择文件后显示摘要",
            "常用算法",
            new[]
            {
                new PresetCard("\uE73E", "SHA-256", "默认算法", true),
                new PresetCard("\uE73E", "MD5", "快速比对"),
                new PresetCard("\uE8C8", "导出报告", "TXT / CSV"),
                new PresetCard("\uE710", "自定义预设", "保存当前参数"),
            },
            "算法",
            new[]
            {
                new FieldOption("SHA-256", "sha256"),
                new FieldOption("SHA-1", "sha1"),
                new FieldOption("MD5", "md5"),
            },
            "比对",
            new[]
            {
                new FieldOption("不比对", "none"),
                new FieldOption("与期望值比对", "expected"),
            },
            "输出格式",
            new[]
            {
                new FieldOption("小写十六进制", "lower"),
                new FieldOption("大写十六进制", "upper"),
            },
            "结果导出",
            true,
            "仅屏显",
            "导出 CSV",
            false,
            string.Empty,
            string.Empty,
            "计算校验值"),

        ["file.metadata"] = new ToolForm(
            "文件与媒体信息",
            "读取容器与编码信息，或清理可识别元数据",
            "\uE946",
            "选择文件后显示媒体参数",
            "信息模式",
            new[]
            {
                new PresetCard("\uE946", "查看信息", "只读", true),
                new PresetCard("\uE74D", "清理元数据", "写入副本"),
                new PresetCard("\uE8C8", "导出报告", "TXT"),
                new PresetCard("\uE710", "自定义预设", "保存当前参数"),
            },
            "模式",
            new[]
            {
                new FieldOption("查看信息", "view"),
                new FieldOption("清理元数据", "clean"),
            },
            "输出",
            new[]
            {
                new FieldOption("屏显 + TXT", "txt"),
                new FieldOption("仅屏显", "none"),
            },
            string.Empty,
            Array.Empty<FieldOption>(),
            string.Empty,
            false,
            string.Empty,
            string.Empty,
            false,
            string.Empty,
            string.Empty,
            "读取文件信息"),
    };
}
