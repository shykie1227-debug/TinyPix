using System.Globalization;

namespace TinyPix.Media.Ffmpeg;

public sealed record FfmpegProgress(
    TimeSpan Position,
    double? Percentage,
    long? Frame,
    double? Speed,
    bool IsComplete);

public sealed class FfmpegProgressParser(TimeSpan? duration)
{
    private readonly Dictionary<string, string> _block =
        new(StringComparer.OrdinalIgnoreCase);

    public FfmpegProgress? ParseLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        int separator = line.IndexOf('=');
        if (separator <= 0)
        {
            return null;
        }

        string key = line[..separator].Trim();
        string value = line[(separator + 1)..].Trim();
        _block[key] = value;
        if (!string.Equals(key, "progress", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        bool complete = string.Equals(value, "end", StringComparison.OrdinalIgnoreCase);
        TimeSpan position = ParsePosition();
        double? percentage = complete
            ? 100
            : CalculatePercentage(position);
        long? frame = ParseLong("frame");
        double? speed = ParseSpeed();
        _block.Clear();

        return new FfmpegProgress(position, percentage, frame, speed, complete);
    }

    private TimeSpan ParsePosition()
    {
        if (_block.TryGetValue("out_time_us", out string? microsecondsText) &&
            long.TryParse(
                microsecondsText,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long microseconds))
        {
            return TimeSpan.FromTicks(Math.Max(0, microseconds) * 10);
        }

        if (_block.TryGetValue("out_time", out string? timestamp) &&
            TimeSpan.TryParse(timestamp, CultureInfo.InvariantCulture, out TimeSpan parsed))
        {
            return parsed < TimeSpan.Zero ? TimeSpan.Zero : parsed;
        }

        return TimeSpan.Zero;
    }

    private double? CalculatePercentage(TimeSpan position)
    {
        if (duration is null || duration <= TimeSpan.Zero)
        {
            return null;
        }

        return Math.Clamp(position.TotalMilliseconds / duration.Value.TotalMilliseconds * 100, 0, 100);
    }

    private long? ParseLong(string key) =>
        _block.TryGetValue(key, out string? text) &&
        long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
            ? value
            : null;

    private double? ParseSpeed()
    {
        if (!_block.TryGetValue("speed", out string? text))
        {
            return null;
        }

        string numeric = text.EndsWith('x') ? text[..^1] : text;
        return double.TryParse(
            numeric,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out double speed)
            ? speed
            : null;
    }
}
