using TinyPix.Media.Ffmpeg;

namespace TinyPix.Media.Tests;

public sealed class FfmpegProgressParserTests
{
    [Fact]
    public void Progress_block_is_emitted_with_clamped_percentage()
    {
        var parser = new FfmpegProgressParser(TimeSpan.FromSeconds(10));

        Assert.Null(parser.ParseLine("frame=75"));
        Assert.Null(parser.ParseLine("out_time_us=2500000"));
        Assert.Null(parser.ParseLine("speed=1.25x"));
        FfmpegProgress? progress = parser.ParseLine("progress=continue");

        Assert.NotNull(progress);
        Assert.Equal(TimeSpan.FromSeconds(2.5), progress.Position);
        Assert.Equal(25, progress.Percentage);
        Assert.Equal(75, progress.Frame);
        Assert.Equal(1.25, progress.Speed);
        Assert.False(progress.IsComplete);
    }

    [Fact]
    public void End_block_is_always_complete()
    {
        var parser = new FfmpegProgressParser(TimeSpan.FromSeconds(10));
        parser.ParseLine("out_time_us=999999");

        FfmpegProgress? progress = parser.ParseLine("progress=end");

        Assert.NotNull(progress);
        Assert.True(progress.IsComplete);
        Assert.Equal(100, progress.Percentage);
    }

    [Fact]
    public void Malformed_values_do_not_break_following_progress()
    {
        var parser = new FfmpegProgressParser(null);
        parser.ParseLine("frame=not-a-number");
        parser.ParseLine("out_time=00:00:03.500000");
        parser.ParseLine("speed=N/A");

        FfmpegProgress? progress = parser.ParseLine("progress=continue");

        Assert.NotNull(progress);
        Assert.Equal(TimeSpan.FromSeconds(3.5), progress.Position);
        Assert.Null(progress.Percentage);
        Assert.Null(progress.Frame);
        Assert.Null(progress.Speed);
    }
}
