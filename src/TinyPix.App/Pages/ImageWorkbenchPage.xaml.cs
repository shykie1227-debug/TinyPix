using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
/// Template 6 / 图片处理 — design frame 4hTWM. One page hosts all eleven frozen
/// image tools: the shared fields (format / size / ratio / quality) sit in the
/// right panel next to the design's own rows, and each tool's full parameter
/// surface stays reachable through the 高级参数 expander generated from
/// <see cref="Forms"/>.
/// </summary>
public sealed partial class ImageWorkbenchPage : Page, IWorkbenchFilePicker
{
    /// <summary>Parameter field kinds rendered by <see cref="FormHost"/>.</summary>
    private enum FieldKind
    {
        Number,
        Text,
        Toggle,
        Choice,
    }

    private sealed class Field
    {
        public required string Key { get; init; }
        public required string Label { get; init; }
        public FieldKind Kind { get; init; }
        public double Default { get; init; }
        public double Minimum { get; init; } = double.MinValue;
        public double Maximum { get; init; } = double.MaxValue;
        public string? Text { get; init; }
        public IReadOnlyList<string>? Options { get; init; }
    }

    private sealed class ToolForm
    {
        public required string Title { get; init; }
        public required string Hint { get; init; }
        public required IReadOnlyList<Field> Fields { get; init; }
    }

    /// <summary>ComboBox row: stable tool id plus its frozen display title.</summary>
    private sealed class ToolItem
    {
        public required string Id { get; init; }
        public required string Title { get; init; }

        public override string ToString() => Title;
    }

    // Frozen tool ids and their parameter surface. Every key here is consumed by
    // the matching TinyPix.Media handler; defaults mirror the frozen matrix.
    private static readonly Dictionary<string, ToolForm> Forms = new(StringComparer.Ordinal)
    {
        ["image.resize"] = new()
        {
            Title = "图片缩放",
            Hint = "按宽高、百分比缩放；默认锁定比例、不放大、Lanczos 高质量滤波。",
            Fields = new[]
            {
                new Field { Key = "width", Label = "目标宽度 (px，0=不限)", Kind = FieldKind.Number, Default = 0, Minimum = 0 },
                new Field { Key = "height", Label = "目标高度 (px，0=不限)", Kind = FieldKind.Number, Default = 0, Minimum = 0 },
                new Field { Key = "percentage", Label = "缩放百分比 (%)", Kind = FieldKind.Number, Default = 0, Minimum = 0 },
                new Field { Key = "keepAspect", Label = "锁定比例", Kind = FieldKind.Toggle, Default = 1 },
                new Field { Key = "noEnlarge", Label = "不放大", Kind = FieldKind.Toggle, Default = 1 },
                new Field
                {
                    Key = "filter", Label = "滤波算法", Kind = FieldKind.Choice, Default = 0,
                    Options = new[] { "lanczos", "mitchell", "catrom", "triangle", "box" },
                },
            },
        },
        ["image.crop"] = new()
        {
            Title = "图片裁切",
            Hint = "显式区域或按比例居中裁切；可顺带做透视校正。",
            Fields = new[]
            {
                new Field { Key = "x", Label = "起点 X (px)", Kind = FieldKind.Number, Default = 0, Minimum = 0 },
                new Field { Key = "y", Label = "起点 Y (px)", Kind = FieldKind.Number, Default = 0, Minimum = 0 },
                new Field { Key = "width", Label = "宽度 (px)", Kind = FieldKind.Number, Default = 0, Minimum = 0 },
                new Field { Key = "height", Label = "高度 (px)", Kind = FieldKind.Number, Default = 0, Minimum = 0 },
                new Field
                {
                    Key = "ratio", Label = "裁切比例", Kind = FieldKind.Choice, Default = 0,
                    Options = new[] { "", "1:1", "4:3", "3:2", "16:9", "9:16" },
                },
                new Field { Key = "straighten", Label = "校正角度 (°)", Kind = FieldKind.Number, Default = 0, Minimum = -45, Maximum = 45 },
            },
        },
        ["image.transform"] = new()
        {
            Title = "旋转与翻转",
            Hint = "90° 倍率旋转或任意角度；支持水平/垂直镜像。",
            Fields = new[]
            {
                new Field
                {
                    Key = "rotate", Label = "旋转角度", Kind = FieldKind.Choice, Default = 0,
                    Options = new[] { "0", "90", "180", "270" },
                },
                new Field { Key = "flipH", Label = "水平镜像", Kind = FieldKind.Toggle, Default = 0 },
                new Field { Key = "flipV", Label = "垂直镜像", Kind = FieldKind.Toggle, Default = 0 },
                new Field { Key = "expand", Label = "扩展画布", Kind = FieldKind.Toggle, Default = 1 },
            },
        },
        ["image.convert"] = new()
        {
            Title = "图片格式转换",
            Hint = "JPG/PNG/WebP/BMP/TIFF/GIF 互转；多页 TIFF 逐页输出。",
            Fields = new[]
            {
                new Field
                {
                    Key = "format", Label = "目标格式", Kind = FieldKind.Choice, Default = 0,
                    Options = new[] { "png", "jpg", "webp", "bmp", "tif", "gif" },
                },
                new Field { Key = "quality", Label = "质量 (1-100)", Kind = FieldKind.Number, Default = 90, Minimum = 1, Maximum = 100 },
            },
        },
        ["image.compress"] = new()
        {
            Title = "图片压缩",
            Hint = "有损质量压缩；默认标准 82。透明图转不支持透明的格式时默认白底。",
            Fields = new[]
            {
                new Field
                {
                    Key = "format", Label = "输出格式", Kind = FieldKind.Choice, Default = 0,
                    Options = new[] { "", "jpg", "webp", "png" },
                },
                new Field { Key = "quality", Label = "质量 (1-100)", Kind = FieldKind.Number, Default = 82, Minimum = 1, Maximum = 100 },
            },
        },
        ["image.color"] = new()
        {
            Title = "亮度与色彩",
            Hint = "亮度/对比度/饱和度/色温/锐化，各 -100–100。",
            Fields = new[]
            {
                new Field { Key = "brightness", Label = "亮度", Kind = FieldKind.Number, Default = 0, Minimum = -100, Maximum = 100 },
                new Field { Key = "contrast", Label = "对比度", Kind = FieldKind.Number, Default = 0, Minimum = -100, Maximum = 100 },
                new Field { Key = "saturation", Label = "饱和度", Kind = FieldKind.Number, Default = 0, Minimum = -100, Maximum = 100 },
                new Field { Key = "temperature", Label = "色温", Kind = FieldKind.Number, Default = 0, Minimum = -100, Maximum = 100 },
                new Field { Key = "sharpen", Label = "锐化", Kind = FieldKind.Number, Default = 0, Minimum = -100, Maximum = 100 },
                new Field { Key = "autoEnhance", Label = "自动增强 (AutoLevel)", Kind = FieldKind.Toggle, Default = 0 },
            },
        },
        ["image.watermark"] = new()
        {
            Title = "图片水印",
            Hint = "文字水印默认“TinyPix”，右下、白字黑描边、70% 不透明；也可用图片水印。",
            Fields = new[]
            {
                new Field { Key = "text", Label = "水印文字", Kind = FieldKind.Text, Text = "TinyPix" },
                new Field { Key = "watermark", Label = "水印图片路径 (可选)", Kind = FieldKind.Text },
                new Field
                {
                    Key = "position", Label = "位置", Kind = FieldKind.Choice, Default = 0,
                    Options = new[] { "bottomRight", "bottomLeft", "topLeft", "topRight", "center" },
                },
                new Field { Key = "opacity", Label = "不透明度 (%)", Kind = FieldKind.Number, Default = 70, Minimum = 0, Maximum = 100 },
                new Field { Key = "sizePercent", Label = "字号 (短边 %)", Kind = FieldKind.Number, Default = 4, Minimum = 1, Maximum = 50 },
                new Field { Key = "tile", Label = "平铺", Kind = FieldKind.Toggle, Default = 0 },
            },
        },
        ["image.exif"] = new()
        {
            Title = "EXIF 查看与清理",
            Hint = "查看不产出文件；清理移除 GPS/相机序列号/作者/注释/内嵌缩略图，保留拍摄时间与方向。",
            Fields = new[]
            {
                new Field
                {
                    Key = "mode", Label = "模式", Kind = FieldKind.Choice, Default = 0,
                    Options = new[] { "view", "clean" },
                },
            },
        },
        ["image.collage"] = new()
        {
            Title = "拼图与联系表",
            Hint = "联系表默认 A4 纵向 300 DPI、3 列；拼图默认 2 列。",
            Fields = new[]
            {
                new Field
                {
                    Key = "mode", Label = "模式", Kind = FieldKind.Choice, Default = 0,
                    Options = new[] { "contact", "grid" },
                },
                new Field { Key = "columns", Label = "列数", Kind = FieldKind.Number, Default = 3, Minimum = 1, Maximum = 10 },
                new Field { Key = "gap", Label = "间距 (px)", Kind = FieldKind.Number, Default = 24, Minimum = 0 },
                new Field { Key = "margin", Label = "页边距 (px)", Kind = FieldKind.Number, Default = 48, Minimum = 0 },
                new Field { Key = "showFilename", Label = "显示文件名", Kind = FieldKind.Toggle, Default = 1 },
            },
        },
        ["image.batch"] = new()
        {
            Title = "图片批量处理",
            Hint = "固定步骤链：方向归一 → 裁切 → 缩放 → 调色 → 水印 → 格式/压缩。",
            Fields = new[]
            {
                new Field
                {
                    Key = "ratio", Label = "裁切比例 (空=不裁切)", Kind = FieldKind.Choice, Default = 0,
                    Options = new[] { "", "1:1", "4:3", "3:2", "16:9" },
                },
                new Field { Key = "width", Label = "缩放宽度 (px，0=不限)", Kind = FieldKind.Number, Default = 0, Minimum = 0 },
                new Field { Key = "height", Label = "缩放高度 (px，0=不限)", Kind = FieldKind.Number, Default = 0, Minimum = 0 },
                new Field { Key = "percentage", Label = "缩放百分比 (%)", Kind = FieldKind.Number, Default = 0, Minimum = 0 },
                new Field { Key = "brightness", Label = "亮度", Kind = FieldKind.Number, Default = 0, Minimum = -100, Maximum = 100 },
                new Field { Key = "contrast", Label = "对比度", Kind = FieldKind.Number, Default = 0, Minimum = -100, Maximum = 100 },
                new Field { Key = "saturation", Label = "饱和度", Kind = FieldKind.Number, Default = 0, Minimum = -100, Maximum = 100 },
                new Field { Key = "temperature", Label = "色温", Kind = FieldKind.Number, Default = 0, Minimum = -100, Maximum = 100 },
                new Field { Key = "sharpen", Label = "锐化", Kind = FieldKind.Number, Default = 0, Minimum = -100, Maximum = 100 },
                new Field { Key = "autoEnhance", Label = "自动增强 (AutoLevel)", Kind = FieldKind.Toggle, Default = 0 },
                new Field { Key = "text", Label = "水印文字 (空=不加水印)", Kind = FieldKind.Text },
                new Field { Key = "opacity", Label = "水印不透明度 (%)", Kind = FieldKind.Number, Default = 70, Minimum = 0, Maximum = 100 },
                new Field { Key = "tile", Label = "水印平铺", Kind = FieldKind.Toggle, Default = 0 },
                new Field
                {
                    Key = "format", Label = "输出格式 (空=保持原格式)", Kind = FieldKind.Choice, Default = 0,
                    Options = new[] { "", "png", "jpg", "webp", "bmp", "tif" },
                },
                new Field { Key = "quality", Label = "质量 (1-100)", Kind = FieldKind.Number, Default = 90, Minimum = 1, Maximum = 100 },
            },
        },
        ["image.background"] = new()
        {
            Title = "人像背景移除与替换",
            Hint = "边界取色 + 容差透明化，适合纯色/影棚背景；复杂场景需 MODNet 模型。",
            Fields = new[]
            {
                new Field
                {
                    Key = "mode", Label = "模式", Kind = FieldKind.Choice, Default = 0,
                    Options = new[] { "remove", "replace" },
                },
                new Field { Key = "fuzz", Label = "容差 (%)", Kind = FieldKind.Number, Default = 12, Minimum = 0, Maximum = 100 },
                new Field { Key = "color", Label = "替换背景色 (#RRGGBB)", Kind = FieldKind.Text, Text = "#FFFFFF" },
                new Field
                {
                    Key = "format", Label = "输出格式 (空=保持原格式)", Kind = FieldKind.Choice, Default = 0,
                    Options = new[] { "", "png", "jpg", "webp", "bmp", "tif" },
                },
            },
        },
    };

    private static readonly string[] ImageExtensions =
    {
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".tif", ".tiff",
    };

    private readonly List<string> _inputPaths = new();
    private readonly Dictionary<string, UIElement> _controls = new(StringComparer.Ordinal);
    private string _toolId = "image.convert";
    private JobSubmissionService? _submission;
    private bool _loading;

    public ImageWorkbenchPage()
    {
        InitializeComponent();

        _loading = true;
        foreach (KeyValuePair<string, ToolForm> pair in Forms.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            ToolBox.Items.Add(new ComboBoxItem { Content = pair.Value.Title, Tag = pair.Key });
        }

        _loading = false;

        Presets.SetPresets(new[]
        {
            new PresetCard("\uE740", "保持原尺寸", "仅换格式", true),
            new PresetCard("\uE774", "社交媒体", "1080 × 1080"),
            new PresetCard("\uE715", "邮件附件", "压缩优先"),
            new PresetCard("\uE710", "自定义预设", "保存当前参数"),
        });

        Presets.PresetInvoked += OnPresetInvoked;
        ToolBox.SelectedIndex = GetToolIndex("image.convert");
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
    }

    private int GetToolIndex(string toolId)
    {
        for (int i = 0; i < ToolBox.Items.Count; i++)
        {
            if (ToolBox.Items[i] is ComboBoxItem item
                && string.Equals(item.Tag as string, toolId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return 0;
    }

    private void SelectTool(string toolId)
    {
        _loading = true;
        ToolBox.SelectedIndex = GetToolIndex(toolId);
        _loading = false;
        ApplyForm(toolId);
    }

    private void ToolList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        if ((ToolBox.SelectedItem as ComboBoxItem)?.Tag is string toolId)
        {
            ApplyForm(toolId);
        }
    }

    private void ApplyForm(string toolId)
    {
        if (!Forms.TryGetValue(toolId, out ToolForm? form))
        {
            return;
        }

        _toolId = toolId;
        BuildForm(form);
        StatusBar.IsOpen = false;

        if (_inputPaths.Count > 0)
        {
            Preview.PreviewSpec = $"{Path.GetExtension(_inputPaths[0]).TrimStart('.').ToUpperInvariant()} · {FormatSize(new FileInfo(_inputPaths[0]).Length)} · {form.Title}";
        }
    }

    private void OnPresetInvoked(object? sender, int index)
    {
        switch (index)
        {
            case 0:
                SizeBox.SelectedIndex = 0;
                RatioBox.SelectedIndex = 0;
                break;
            case 1:
                SizeBox.SelectedIndex = 3;
                RatioBox.SelectedIndex = 1;
                FormatBox.SelectedIndex = 1;
                break;
            case 2:
                SizeBox.SelectedIndex = 5;
                QualitySlider.Value = 70;
                FormatBox.SelectedIndex = 1;
                break;
        }
    }

    private void BuildForm(ToolForm form)
    {
        FormHost.Children.Clear();
        _controls.Clear();

        foreach (Field field in form.Fields)
        {
            UIElement control = field.Kind switch
            {
                FieldKind.Number => BuildNumberBox(field),
                FieldKind.Text => BuildTextBox(field),
                FieldKind.Toggle => BuildToggle(field),
                _ => BuildComboBox(field),
            };

            _controls[field.Key] = control;
            FormHost.Children.Add(control);
        }
    }

    private static NumberBox BuildNumberBox(Field field)
    {
        var box = new NumberBox
        {
            Header = field.Label,
            Value = field.Default,
            Minimum = field.Minimum,
            Maximum = field.Maximum,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Application.Current.Resources["TinyPixCjkFontFamily"],
            FontSize = 12,
        };
        AutomationProperties.SetName(box, field.Label);
        return box;
    }

    private static TextBox BuildTextBox(Field field)
    {
        var box = new TextBox
        {
            Header = field.Label,
            Text = field.Text ?? string.Empty,
            Style = (Style)Application.Current.Resources["TinyPixTextBoxStyle"],
            FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Application.Current.Resources["TinyPixCjkFontFamily"],
            FontSize = 12,
        };
        AutomationProperties.SetName(box, field.Label);
        return box;
    }

    private static ToggleSwitch BuildToggle(Field field)
    {
        var toggle = new ToggleSwitch
        {
            Header = field.Label,
            IsOn = field.Default > 0.5,
            OnContent = "开",
            OffContent = "关",
            Style = (Style)Application.Current.Resources["TinyPixToggleSwitchStyle"],
            FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Application.Current.Resources["TinyPixCjkFontFamily"],
            FontSize = 11,
        };
        AutomationProperties.SetName(toggle, field.Label);
        return toggle;
    }

    private static ComboBox BuildComboBox(Field field)
    {
        var combo = new ComboBox
        {
            Header = field.Label,
            Style = (Style)Application.Current.Resources["TinyPixComboBoxStyle"],
            FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Application.Current.Resources["TinyPixCjkFontFamily"],
            FontSize = 11,
        };
        AutomationProperties.SetName(combo, field.Label);
        foreach (string option in field.Options ?? Array.Empty<string>())
        {
            combo.Items.Add(new ComboBoxItem { Content = option });
        }

        combo.SelectedIndex = Math.Clamp((int)field.Default, 0, Math.Max(0, combo.Items.Count - 1));
        return combo;
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
            Preview.PreviewSpec = "支持 JPG / PNG / WebP / BMP / TIFF";
            LeftFilePanelControl.Current?.ShowFile(null, null, null);
            return;
        }

        string first = _inputPaths[0];
        string name = Path.GetFileName(first);
        long bytes = new FileInfo(first).Length;
        string kind = Path.GetExtension(first).TrimStart('.').ToUpperInvariant();

        Preview.PreviewName = _inputPaths.Count == 1 ? name : $"{name} 等 {_inputPaths.Count} 个文件";
        Preview.PreviewSpec = $"{kind} · {FormatSize(bytes)} · {Forms[_toolId].Title}";
        OutputPathText.Text = Path.GetDirectoryName(first) ?? "与原文件相同";
        LeftFilePanelControl.Current?.ShowFile(name, $"{FormatSize(bytes)}\n{kind}", "等待处理");
    }

    private void OnQualityChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        QualityValueText.Text = $"{(int)Math.Round(e.NewValue)}%";
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        FormatBox.SelectedIndex = 0;
        SizeBox.SelectedIndex = 0;
        RatioBox.SelectedIndex = 0;
        QualitySlider.Value = 90;
        HardwareSwitch.IsOn = true;
        StatusBar.IsOpen = false;
        ApplyForm(_toolId);
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_submission is null)
        {
            return;
        }

        if (_inputPaths.Count == 0)
        {
            SetStatus("请先添加至少一张图片。", InfoBarSeverity.Warning);
            return;
        }

        if (!Forms.TryGetValue(_toolId, out ToolForm? form))
        {
            return;
        }

        Dictionary<string, string> parameters = CollectParameters(form);
        ApplySharedParameters(parameters);

        SetStatus("正在提交任务…", InfoBarSeverity.Informational);
        TaskQueueControl.Current?.ShowTask(Path.GetFileName(_inputPaths[0]), $"{form.Title} · 正在处理", 0);

        int succeeded = 0;
        var lastError = string.Empty;
        foreach (string input in _inputPaths)
        {
            JobRequest request = JobRequest.Create(
                _toolId,
                new[] { input },
                Path.GetDirectoryName(input)!,
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

        TaskQueueControl.Current?.ShowIdle();

        if (succeeded == _inputPaths.Count)
        {
            SetStatus($"完成：{succeeded} 张图片已处理。", InfoBarSeverity.Success);
        }
        else
        {
            SetStatus($"已完成 {succeeded}/{_inputPaths.Count} 张；{lastError}", InfoBarSeverity.Error);
        }
    }

    /// <summary>
    /// Layers the always-visible panel rows (format / size / ratio / quality) on
    /// top of the tool's advanced parameters. Tools that do not consume a key
    /// simply ignore it.
    /// </summary>
    private void ApplySharedParameters(Dictionary<string, string> parameters)
    {
        if ((FormatBox.SelectedItem as ComboBoxItem)?.Tag is string format && format.Length > 0)
        {
            parameters["format"] = format;
        }

        if ((SizeBox.SelectedItem as ComboBoxItem)?.Tag is string size)
        {
            string[] parts = size.Split('x');
            if (parts.Length == 2
                && int.TryParse(parts[0], out int width)
                && int.TryParse(parts[1], out int height)
                && width > 0
                && height > 0)
            {
                parameters["width"] = width.ToString(CultureInfo.InvariantCulture);
                parameters["height"] = height.ToString(CultureInfo.InvariantCulture);
            }
        }

        if ((RatioBox.SelectedItem as ComboBoxItem)?.Tag is string ratio && ratio.Length > 0)
        {
            parameters["ratio"] = ratio;
        }

        parameters["quality"] = ((int)Math.Round(QualitySlider.Value)).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Reads the generated advanced controls back into the handler parameter
    /// dictionary. Empty text and empty choices are omitted so defaults apply.
    /// </summary>
    private Dictionary<string, string> CollectParameters(ToolForm form)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Field field in form.Fields)
        {
            if (!_controls.TryGetValue(field.Key, out UIElement? control))
            {
                continue;
            }

            switch (control)
            {
                case NumberBox number:
                    if (number.Value > number.Minimum || field.Default != 0)
                    {
                        parameters[field.Key] = number.Value.ToString(CultureInfo.InvariantCulture);
                    }

                    break;

                case TextBox text:
                    if (!string.IsNullOrWhiteSpace(text.Text))
                    {
                        parameters[field.Key] = text.Text.Trim();
                    }

                    break;

                case ToggleSwitch toggle:
                    parameters[field.Key] = toggle.IsOn ? "1" : "0";
                    break;

                case ComboBox combo:
                    if (combo.SelectedItem is ComboBoxItem item && !string.IsNullOrEmpty(item.Content as string))
                    {
                        parameters[field.Key] = item.Content as string ?? string.Empty;
                    }

                    break;
            }
        }

        return parameters;
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
