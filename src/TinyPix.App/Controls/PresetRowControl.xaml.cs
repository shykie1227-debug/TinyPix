using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace TinyPix.App.Controls;

/// <summary>One preset card: icon glyph, title, caption and selected state.</summary>
public sealed record PresetCard(string Glyph, string Title, string Subtitle, bool IsSelected = false);

/// <summary>
/// design: Common Presets — section title + four preset cards. Cards are built
/// in code so each page can describe its presets declaratively.
/// </summary>
public sealed partial class PresetRowControl : UserControl
{
    private readonly List<PresetCard> _presets = new();
    private readonly List<Border> _cards = new();

    public PresetRowControl()
    {
        InitializeComponent();
    }

    /// <summary>Raised with the zero-based index of the tapped preset card.</summary>
    public event EventHandler<int>? PresetInvoked;

    public string SectionTitle
    {
        get => SectionTitleText.Text;
        set => SectionTitleText.Text = value;
    }

    public void SetPresets(IReadOnlyList<PresetCard> presets)
    {
        _presets.Clear();
        _cards.Clear();
        PresetGrid.Children.Clear();

        for (int i = 0; i < presets.Count && i < 4; i++)
        {
            PresetCard preset = presets[i];
            _presets.Add(preset);

            Border card = BuildCard(preset, i);
            _cards.Add(card);
            Grid.SetColumn(card, i);
            PresetGrid.Children.Add(card);
        }
    }

    /// <summary>Highlights one card and clears the rest (design: lime border).</summary>
    public void Select(int index)
    {
        for (int i = 0; i < _cards.Count; i++)
        {
            bool selected = i == index;
            _cards[i].BorderBrush = Lookup(selected ? "TinyPixLimeBrush" : "TinyPixControlBorderBrush");

            if (_cards[i].Child is StackPanel stack && stack.Children.Count > 0 && stack.Children[0] is FontIcon icon)
            {
                icon.Foreground = Lookup(selected ? "TinyPixDeepLimeBrush" : "TinyPixMutedBrush");
            }
        }
    }

    private Border BuildCard(PresetCard preset, int index)
    {
        var icon = new FontIcon
        {
            Glyph = preset.Glyph,
            FontSize = 16,
            Foreground = Lookup(preset.IsSelected ? "TinyPixDeepLimeBrush" : "TinyPixMutedBrush"),
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        var title = new TextBlock
        {
            Text = preset.Title,
            FontFamily = (FontFamily)Application.Current.Resources["TinyPixCjkFontFamily"],
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = Lookup("TinyPixInkBrush"),
            TextWrapping = TextWrapping.Wrap,
        };

        var subtitle = new TextBlock
        {
            Text = preset.Subtitle,
            FontFamily = (FontFamily)Application.Current.Resources["TinyPixCjkFontFamily"],
            FontSize = 9,
            Foreground = Lookup("TinyPixMutedBrush"),
            TextWrapping = TextWrapping.Wrap,
        };

        var stack = new StackPanel { Spacing = 5 };
        stack.Children.Add(icon);
        stack.Children.Add(title);
        stack.Children.Add(subtitle);

        var card = new Border
        {
            Background = Lookup("TinyPixSurfaceBrush"),
            BorderBrush = Lookup(preset.IsSelected ? "TinyPixLimeBrush" : "TinyPixControlBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(12),
            Child = stack,
        };

        card.Tapped += (_, _) =>
        {
            Select(index);
            PresetInvoked?.Invoke(this, index);
        };

        return card;
    }

    private static Brush Lookup(string key) =>
        (Brush)Application.Current.Resources[key];
}
