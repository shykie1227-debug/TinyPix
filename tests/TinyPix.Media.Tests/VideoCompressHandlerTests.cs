using System.Collections.Generic;
using System.IO;
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

        public string FfprobeExecutablePath => "ffprobe";
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
        Assert.Equal(Path.Combine("/out", "clip_压缩.mp4"), args[^1]);
    }

    [Fact]
    public void Compress_without_source_bitrate_falls_back_to_lgpl_bitrate()
    {
        var handler = new VideoCompressHandler(new FakeEngine("ffmpeg"));
        IReadOnlyList<string> args = handler.PreviewArguments(
            "clip.mp4", "/out", new Dictionary<string, string> { ["format"] = "mp4", ["quality"] = "60" });

        int codec = args.ToList().IndexOf("-c:v");
        Assert.Equal(LgplVideoCodec.H264, args[codec + 1]);
        int bv = args.ToList().IndexOf("-b:v");
        Assert.Equal("6300k", args[bv + 1]); // 300 + 60 * 100
        Assert.Equal(Path.Combine("/out", "clip_压缩.mp4"), args[^1]);
    }
}
