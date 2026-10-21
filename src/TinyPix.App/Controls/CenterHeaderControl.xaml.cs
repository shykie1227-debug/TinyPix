using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TinyPix.App.Controls;

/// <summary>
/// design: centerHeader — page title + one-line subtitle on the left, the
/// "适应窗口" secondary button on the right.
/// </summary>
public sealed partial class CenterHeaderControl : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(CenterHeaderControl), new PropertyMetadata(string.Empty, OnTitleChanged));

    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle), typeof(string), typeof(CenterHeaderControl), new PropertyMetadata(string.Empty, OnSubtitleChanged));

    public CenterHeaderControl()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((CenterHeaderControl)d).TitleText.Text = e.NewValue as string ?? string.Empty;

    private static void OnSubtitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((CenterHeaderControl)d).SubtitleText.Text = e.NewValue as string ?? string.Empty;
}
