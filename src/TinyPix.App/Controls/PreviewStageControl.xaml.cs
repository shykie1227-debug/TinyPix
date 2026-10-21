using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TinyPix.App.Controls;

/// <summary>
/// design: Video Preview Card — dark stage with a play circle, a category icon,
/// a timeline rail and the previewMeta row (file name + technical spec).
/// </summary>
public sealed partial class PreviewStageControl : UserControl
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(PreviewStageControl), new PropertyMetadata("\uE768", OnGlyphChanged));

    public static readonly DependencyProperty PreviewNameProperty = DependencyProperty.Register(
        nameof(PreviewName), typeof(string), typeof(PreviewStageControl), new PropertyMetadata("尚未添加文件", OnNameChanged));

    public static readonly DependencyProperty PreviewSpecProperty = DependencyProperty.Register(
        nameof(PreviewSpec), typeof(string), typeof(PreviewStageControl), new PropertyMetadata("添加文件后在此预览", OnSpecChanged));

    public static readonly DependencyProperty ShowTimelineProperty = DependencyProperty.Register(
        nameof(ShowTimeline), typeof(bool), typeof(PreviewStageControl), new PropertyMetadata(true, OnShowTimelineChanged));

    public static readonly DependencyProperty CardHeightProperty = DependencyProperty.Register(
        nameof(CardHeight), typeof(double), typeof(PreviewStageControl), new PropertyMetadata(356d, OnCardHeightChanged));

    public PreviewStageControl()
    {
        InitializeComponent();
    }

    /// <summary>Design uses 356 on most templates and 286 on the timeline page.</summary>
    public double CardHeight
    {
        get => (double)GetValue(CardHeightProperty);
        set => SetValue(CardHeightProperty, value);
    }

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public string PreviewName
    {
        get => (string)GetValue(PreviewNameProperty);
        set => SetValue(PreviewNameProperty, value);
    }

    public string PreviewSpec
    {
        get => (string)GetValue(PreviewSpecProperty);
        set => SetValue(PreviewSpecProperty, value);
    }

    public bool ShowTimeline
    {
        get => (bool)GetValue(ShowTimelineProperty);
        set => SetValue(ShowTimelineProperty, value);
    }

    /// <summary>Replaces the placeholder icon with real preview content.</summary>
    public void SetPreviewContent(object? content)
    {
        StageContentHost.Content = content;
        PlaceholderPanel.Visibility = content is null ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetTimeRange(string start, string end)
    {
        TimeStartText.Text = start;
        TimeEndText.Text = end;
    }

    private static void OnGlyphChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((PreviewStageControl)d).StageGlyph.Glyph = e.NewValue as string ?? string.Empty;

    private static void OnNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((PreviewStageControl)d).PreviewNameText.Text = e.NewValue as string ?? string.Empty;

    private static void OnSpecChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((PreviewStageControl)d).PreviewSpecText.Text = e.NewValue as string ?? string.Empty;

    private static void OnShowTimelineChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((PreviewStageControl)d).TimelinePanel.Visibility =
            (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;

    private static void OnCardHeightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((PreviewStageControl)d).CardBorder.Height = (double)e.NewValue;
}
