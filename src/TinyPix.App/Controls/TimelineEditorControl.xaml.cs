using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace TinyPix.App.Controls;

/// <summary>One named span on the single non-destructive track.</summary>
public sealed class TimelineSegment
{
    public TimelineSegment(double start, double end, bool kept = true)
    {
        Start = start;
        End = end;
        Kept = kept;
    }

    public double Start { get; set; }

    public double End { get; set; }

    public bool Kept { get; set; }

    public double Length => Math.Max(0, End - Start);

    public TimelineSegment Clone() => new(Start, End, Kept);
}

/// <summary>
/// design: Timeline Editor — non-destructive single track with in/out points, a
/// playhead, per-segment exclusion, undo/redo and zoom. Everything is rendered
/// onto one Canvas so the layout matches the design's absolute positioning.
/// </summary>
public sealed partial class TimelineEditorControl : UserControl
{
    private const double MinSegmentSeconds = 0.2;

    private readonly List<TimelineSegment> _segments = new();
    private readonly Stack<EditSnapshot> _undo = new();
    private readonly Stack<EditSnapshot> _redo = new();

    private double _duration;
    private double _position;
    private double _inPoint;
    private double _outPoint;
    private double _zoom = 1;
    private bool _suppressRender;

    public TimelineEditorControl()
    {
        InitializeComponent();
        Reset(0);
    }

    /// <summary>Raised whenever in/out, segments or position change.</summary>
    public event EventHandler? StateChanged;

    public double Duration => _duration;

    public double PositionSeconds => _position;

    public double InSeconds => _inPoint;

    public double OutSeconds => _outPoint;

    public bool HasSelection => _inPoint > 0.001 || _outPoint < _duration - 0.001;

    public int KeptCount
    {
        get
        {
            int count = 0;
            foreach (TimelineSegment segment in _segments)
            {
                if (segment.Kept)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>Loads a new media duration and resets every edit.</summary>
    public void Load(double durationSeconds)
    {
        _duration = Math.Max(0, durationSeconds);
        Reset(_duration);
    }

    public void SetPosition(double seconds)
    {
        _position = Math.Clamp(seconds, 0, _duration);
        Render();
        UpdateTexts();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SeekBy(double deltaSeconds) => SetPosition(_position + deltaSeconds);

    public void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        _redo.Push(Snapshot());
        Restore(_undo.Pop());
    }

    public void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        _undo.Push(Snapshot());
        Restore(_redo.Pop());
    }

    // ------------------------------------------------------------- commands

    private void OnSetInClick(object sender, RoutedEventArgs e) => SetInAtPlayhead();

    private void OnSetOutClick(object sender, RoutedEventArgs e)
    {
        PushUndo();
        _outPoint = Math.Max(_inPoint + MinSegmentSeconds, _position);
        _outPoint = Math.Min(_outPoint, _duration);
        AfterEdit();
    }

    public void SetInAtPlayhead()
    {
        PushUndo();
        _inPoint = Math.Min(_position, Math.Max(0, _outPoint - MinSegmentSeconds));
        AfterEdit();
    }

    private void OnSplitClick(object sender, RoutedEventArgs e) => SplitAtPlayhead();

    public void SplitAtPlayhead()
    {
        if (_duration <= 0)
        {
            return;
        }

        // Find the segment under the playhead and split it in two.
        for (int i = 0; i < _segments.Count; i++)
        {
            TimelineSegment segment = _segments[i];
            if (_position > segment.Start + MinSegmentSeconds
                && _position < segment.End - MinSegmentSeconds)
            {
                PushUndo();
                bool kept = segment.Kept;
                _segments[i] = new TimelineSegment(segment.Start, _position, kept);
                _segments.Insert(i + 1, new TimelineSegment(_position, segment.End, kept));
                AfterEdit();
                return;
            }
        }
    }

    private void OnToggleSegmentClick(object sender, RoutedEventArgs e) => ToggleSegmentAtPlayhead();

    public void ToggleSegmentAtPlayhead()
    {
        for (int i = 0; i < _segments.Count; i++)
        {
            TimelineSegment segment = _segments[i];
            if (_position >= segment.Start && _position <= segment.End)
            {
                PushUndo();
                segment.Kept = !segment.Kept;
                AfterEdit();
                return;
            }
        }
    }

    private void OnZoomInClick(object sender, RoutedEventArgs e)
    {
        _zoom = Math.Min(4, _zoom * 1.5);
        Render();
    }

    private void OnZoomOutClick(object sender, RoutedEventArgs e)
    {
        _zoom = Math.Max(1, _zoom / 1.5);
        Render();
    }

    private void OnUndoClick(object sender, RoutedEventArgs e) => Undo();

    private void OnRedoClick(object sender, RoutedEventArgs e) => Redo();

    // --------------------------------------------------------------- editing

    private void Reset(double duration)
    {
        _segments.Clear();
        _undo.Clear();
        _redo.Clear();
        _position = 0;
        _inPoint = 0;
        _outPoint = duration;
        _zoom = 1;

        if (duration > 0)
        {
            _segments.Add(new TimelineSegment(0, duration));
        }

        Render();
        UpdateTexts();
    }

    private void PushUndo()
    {
        _undo.Push(Snapshot());
        _redo.Clear();
    }

    private EditSnapshot Snapshot()
    {
        var copy = new List<TimelineSegment>(_segments.Count);
        foreach (TimelineSegment segment in _segments)
        {
            copy.Add(segment.Clone());
        }

        return new EditSnapshot(copy, _inPoint, _outPoint, _position);
    }

    private void Restore(EditSnapshot snapshot)
    {
        _segments.Clear();
        foreach (TimelineSegment segment in snapshot.Segments)
        {
            _segments.Add(segment.Clone());
        }

        _inPoint = snapshot.InPoint;
        _outPoint = snapshot.OutPoint;
        _position = snapshot.Position;
        AfterEdit();
    }

    private void AfterEdit()
    {
        Render();
        UpdateTexts();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------------- rendering

    private void OnTrackSizeChanged(object sender, SizeChangedEventArgs e) => Render();

    private void OnTrackPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_duration <= 0)
        {
            return;
        }

        double x = e.GetCurrentPoint(TrackCanvas).Position.X;
        double width = TrackCanvas.ActualWidth * _zoom;
        if (width <= 0)
        {
            return;
        }

        SetPosition(x / width * _duration);
    }

    private void UpdateTexts()
    {
        InPointText.Text = FormatTime(_inPoint);
        OutPointText.Text = FormatTime(_outPoint);
        PlayheadText.Text = FormatTime(_position);
        SummaryText.Text = $"播放头 {FormatTime(_position)} / {FormatTime(_duration)}";
        HeaderText.Text = _duration <= 0
            ? "非破坏单轨 · 等待媒体"
            : $"非破坏单轨 · {KeptCount} 段保留";

        Ruler0.Text = ShortTime(0);
        Ruler1.Text = ShortTime(_duration * 0.25);
        Ruler2.Text = ShortTime(_duration * 0.5);
        Ruler3.Text = ShortTime(_duration * 0.75);
        Ruler4.Text = ShortTime(_duration);

        UndoButton.IsEnabled = _undo.Count > 0;
        RedoButton.IsEnabled = _redo.Count > 0;
    }

    private void Render()
    {
        if (_suppressRender)
        {
            return;
        }

        _suppressRender = true;
        try
        {
            TrackCanvas.Children.Clear();

            double width = TrackCanvas.ActualWidth;
            double height = TrackCanvas.ActualHeight;
            if (width <= 1 || height <= 1)
            {
                return;
            }

            double span = _duration > 0 ? _duration : 1;
            double scale = width * _zoom / span;

            double Double2X(double seconds) => seconds * scale;

            DrawThumbnails(width, height);
            DrawWaveform(width, height);

            if (_duration <= 0)
            {
                AddLabel("添加文件后显示时间轴", 8, height / 2 - 9, width - 16, 18, null, Lookup("TinyPixOnMediaBrush"), 9, FontWeights.Normal, HorizontalAlignment.Center);
                return;
            }

            DrawOutOfRange(scale);

            foreach (TimelineSegment segment in _segments)
            {
                double left = Double2X(segment.Start);
                double segmentWidth = Math.Max(3, Double2X(segment.Length));
                DrawSegment(segment, left, segmentWidth);
            }

            DrawMarker(_inPoint, scale, "I");
            DrawMarker(_outPoint, scale, "O");
            DrawPlayhead(_position, scale, height);
        }
        finally
        {
            _suppressRender = false;
        }
    }

    private void DrawThumbnails(double width, double height)
    {
        const double top = 40;
        double blockWidth = Math.Max(24, width / 9);
        int index = 0;
        for (double x = 0; x < width; x += blockWidth)
        {
            var block = new Rectangle
            {
                Width = Math.Min(blockWidth, width - x),
                Height = 30,
                Fill = Solid(index % 2 == 0 ? Color.FromArgb(255, 0x30, 0x31, 0x38) : Color.FromArgb(255, 0x25, 0x26, 0x2C)),
            };
            Canvas.SetLeft(block, x);
            Canvas.SetTop(block, top);
            TrackCanvas.Children.Add(block);

            var horizon = new Rectangle
            {
                Width = block.Width,
                Height = 6,
                Fill = Solid(index % 2 == 0 ? Color.FromArgb(255, 0x3B, 0x41, 0x48) : Color.FromArgb(255, 0x34, 0x37, 0x3D)),
            };
            Canvas.SetLeft(horizon, x);
            Canvas.SetTop(horizon, top + 12);
            TrackCanvas.Children.Add(horizon);

            index++;
        }
    }

    private void DrawWaveform(double width, double height)
    {
        // Deterministic pseudo waveform so the layout is stable across renders.
        var brush = Solid(Color.FromArgb(140, 0xFF, 0xFF, 0xFF));
        double x = 6;
        int i = 0;
        while (x < width - 6)
        {
            double barHeight = 4 + ((i * 37) % 13);
            var bar = new Rectangle
            {
                Width = 2,
                Height = barHeight,
                RadiusX = 1,
                RadiusY = 1,
                Fill = brush,
            };
            Canvas.SetLeft(bar, x);
            Canvas.SetTop(bar, 68 - barHeight);
            TrackCanvas.Children.Add(bar);
            x += 8;
            i++;
        }
    }

    private void DrawOutOfRange(double scale)
    {
        if (_inPoint > 0.01)
        {
            DrawRange(0, _inPoint, scale, "范围外");
        }

        if (_outPoint < _duration - 0.01)
        {
            DrawRange(_outPoint, _duration, scale, "范围外");
        }
    }

    private void DrawRange(double start, double end, double scale, string label)
    {
        double left = start * scale;
        double width = Math.Max(2, (end - start) * scale);
        var border = new Border
        {
            Width = width,
            Height = 28,
            Background = Solid(Color.FromArgb(255, 0x20, 0x21, 0x26)),
            BorderBrush = Lookup("TinyPixOnMediaBrush"),
            BorderThickness = new Thickness(1),
        };
        if (width > 46)
        {
            border.Child = new TextBlock
            {
                Text = label,
                FontFamily = (FontFamily)Application.Current.Resources["TinyPixCjkFontFamily"],
                FontSize = 7,
                FontWeight = FontWeights.SemiBold,
                Foreground = Lookup("TinyPixOnMediaBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        Canvas.SetLeft(border, left);
        Canvas.SetTop(border, 6);
        TrackCanvas.Children.Add(border);
    }

    private void DrawSegment(TimelineSegment segment, double left, double width)
    {
        bool highlighted = segment.Kept
            && _position >= segment.Start
            && _position <= segment.End;

        var border = new Border
        {
            Width = width,
            Height = 28,
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(8, 0, 8, 0),
            Background = segment.Kept
                ? (highlighted ? Lookup("TinyPixLimeBrush") : Lookup("TinyPixLimeSoftBrush"))
                : Solid(Color.FromArgb(255, 0x35, 0x36, 0x3B)),
            BorderBrush = segment.Kept
                ? (highlighted ? Lookup("TinyPixFocusBrush") : Lookup("TinyPixLimeBrush"))
                : Lookup("TinyPixOnMediaBrush"),
            BorderThickness = new Thickness(1),
        };

        if (width > 52)
        {
            int index = _segments.IndexOf(segment) + 1;
            string label = segment.Kept
                ? $"{index} 保留 · {ShortTime(segment.Start)}–{ShortTime(segment.End)}"
                : $"{index} 排除";

            border.Child = new TextBlock
            {
                Text = label,
                FontFamily = (FontFamily)Application.Current.Resources["TinyPixMonoFontFamily"],
                FontSize = 8,
                FontWeight = FontWeights.SemiBold,
                Foreground = segment.Kept && highlighted
                    ? Lookup("TinyPixOnLimeBrush")
                    : (segment.Kept ? Lookup("TinyPixInkBrush") : Lookup("TinyPixOnMediaBrush")),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
        }

        Canvas.SetLeft(border, left);
        Canvas.SetTop(border, 6);
        TrackCanvas.Children.Add(border);
    }

    private void DrawMarker(double seconds, double scale, string tag)
    {
        double x = seconds * scale;

        var line = new Rectangle
        {
            Width = 2,
            Height = 60,
            Fill = Lookup("TinyPixLimeBrush"),
        };
        Canvas.SetLeft(line, x);
        Canvas.SetTop(line, 4);
        TrackCanvas.Children.Add(line);

        AddLabel(tag, x - 6, 0, 14, 14, Lookup("TinyPixLimeBrush"), Lookup("TinyPixOnLimeBrush"), 8, FontWeights.Bold, HorizontalAlignment.Center);
    }

    private void DrawPlayhead(double seconds, double scale, double height)
    {
        double x = seconds * scale;

        var line = new Rectangle
        {
            Width = 2,
            Height = height,
            Fill = Lookup("TinyPixOnMediaBrush"),
        };
        Canvas.SetLeft(line, x);
        Canvas.SetTop(line, 0);
        TrackCanvas.Children.Add(line);

        var handle = new Ellipse
        {
            Width = 12,
            Height = 12,
            Fill = Lookup("TinyPixOnMediaBrush"),
            Stroke = Lookup("TinyPixMediaStageBrush"),
            StrokeThickness = 1,
        };
        Canvas.SetLeft(handle, x - 5);
        Canvas.SetTop(handle, 0);
        TrackCanvas.Children.Add(handle);
    }

    private void AddLabel(
        string text,
        double left,
        double top,
        double width,
        double height,
        Brush? background,
        Brush foreground,
        double fontSize,
        Windows.UI.Text.FontWeight weight,
        HorizontalAlignment alignment)
    {
        var block = new Border
        {
            Width = width,
            Height = height,
            Background = background,
        };

        block.Child = new TextBlock
        {
            Text = text,
            FontFamily = (FontFamily)Application.Current.Resources["TinyPixCjkFontFamily"],
            FontSize = fontSize,
            FontWeight = weight,
            Foreground = foreground,
            HorizontalAlignment = alignment,
            VerticalAlignment = VerticalAlignment.Center,
        };

        Canvas.SetLeft(block, left);
        Canvas.SetTop(block, top);
        TrackCanvas.Children.Add(block);
    }

    // --------------------------------------------------------------- helpers

    private static SolidColorBrush Solid(Color color) => new(color);

    private static Brush Lookup(string key) => (Brush)Application.Current.Resources[key];

    private static string FormatTime(double seconds)
    {
        if (double.IsNaN(seconds) || seconds < 0)
        {
            seconds = 0;
        }

        var span = TimeSpan.FromSeconds(seconds);
        return $"{(int)span.TotalMinutes:00}:{span.Seconds:00}.{span.Milliseconds:000}";
    }

    private static string ShortTime(double seconds)
    {
        if (double.IsNaN(seconds) || seconds < 0)
        {
            seconds = 0;
        }

        var span = TimeSpan.FromSeconds(seconds);
        return string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalMinutes:00}:{span.Seconds:00}");
    }

    private sealed record EditSnapshot(
        List<TimelineSegment> Segments,
        double InPoint,
        double OutPoint,
        double Position);
}
