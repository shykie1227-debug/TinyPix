using System.Collections.Generic;
using System.Linq;
using TinyPix.Media.Ffmpeg;
using TinyPix.Media.Video;

namespace TinyPix.Media.Tests;

public sealed class VideoCompressHandlerTests
{
    private sealed class FakeEngine : IFfmpegEngineProvider
    {
        public FakeEngine(string path) => FfmpegExecutablePath = path;

        public string FfmpegExecutablePath { get; }
    }

    [Fact]
    public void Compress_with_source_bitrate_uses_60_percent_target()
    {
        var handler = new VideoCompressHandler(new FakeEngine("ffmpeg"));
        IReadOnlyList<string> args = handler.PreviewArguments(
            "clip.mp4", "/out", new Dictionary<string, string> { ["format"] = "mp4", ["sourceBitrateKbps"] = "10000" });

        int bv = args.ToList().IndexOf("-b:v");
        Assert.Equal("6000k", args[bv + 1]);
        int max = args.ToList().IndexOf("-maxrate");
        Assert.Equal("6000k", args[max + 1]);
        int buf = args.ToList().IndexOf("-bufsize");
        Assert.Equal("12000k", args[buf + 1]);
        Assert.Equal("/out/clip_压缩.mp4", args[^1]);
    }

    [Fact]
    public void Compress_without_source_bitrate_falls_back_to_crf()
    {
        var handler = new VideoCompressHandler(new FakeEngine("ffmpeg"));
        IReadOnlyList<string> args = handler.PreviewArguments(
            "clip.mp4", "/out", new Dictionary<string, string> { ["format"] = "mp4", ["quality"] = "60" });

        int crf = args.ToList().IndexOf("-crf");
        Assert.Equal("22", args[crf + 1]); // 28 - round(60 * 0.10) = 22
        Assert.Equal("/out/clip_压缩.mp4", args[^1]);
    }
}
