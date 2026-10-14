using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TinyPix.Core.Jobs;
using TinyPix.Core.Tools;
using TinyPix.Media.Ffmpeg;
using TinyPix.Media.Video;
using Xunit;

namespace TinyPix.Media.Tests;

/// <summary>
/// Contract tests for the Phase 3 video-edit handlers. The fake engine lets us
/// exercise BuildJobs/ValidateAsync without a real FFmpeg binary; execution paths
/// run on the exe machine via the published build.
/// </summary>
public sealed class VideoEditHandlersTests
{
    private sealed class FakeEngine : IFfmpegEngineProvider
    {
        public string FfmpegExecutablePath => "ffmpeg";
    }

    private static JobRequest Request(
        string toolId, IReadOnlyList<string> inputs, Dictionary<string, string>? parameters = null) =>
        JobRequest.Create(toolId, inputs, Path.GetTempPath(), JobWorkload.HeavyMedia, parameters);

    [Fact]
    public async Task Trim_KeyframeCopy_BuildsSsToCopyArgs()
    {
        var handler = new VideoTrimHandler(new FakeEngine());
        JobRequest request = Request("video.trim", new[] { "/v/clip.mp4" },
            new() { ["startSecs"] = "0", ["endSecs"] = "5" });

        IReadOnlyList<string> args = handler.PreviewArguments(request);

        Assert.Contains("-ss", args);
        Assert.Contains("-to", args);
        Assert.Contains("-c", args);
        Assert.Contains("copy", args);
        Assert.Contains("_剪辑", handler.PreviewOutputPaths(request)[0]);
    }

    [Fact]
    public async Task Trim_Precise_ReEncodesToMp4()
    {
        var handler = new VideoTrimHandler(new FakeEngine());
        JobRequest request = Request("video.trim", new[] { "/v/clip.mkv" },
            new() { ["startSecs"] = "1", ["endSecs"] = "4", ["precise"] = "1" });

        IReadOnlyList<string> args = handler.PreviewArguments(request);

        Assert.Contains(LgplVideoCodec.H264, args);
        Assert.Contains(LgplVideoCodec.BitrateKbpsForQuality(LgplVideoCodec.DefaultQuality) + "k", args);
        Assert.Contains("aac", args);
        Assert.EndsWith("_剪辑.mp4", handler.PreviewOutputPaths(request)[0]);
    }

    [Fact]
    public async Task Split_BuildsNumberedSegments()
    {
        var handler = new VideoSplitHandler(new FakeEngine());
        JobRequest request = Request("video.split", new[] { "/v/clip.mp4" },
            new() { ["points"] = "2.5,5" });

        IReadOnlyList<string> outputs = handler.PreviewOutputPaths(request);

        Assert.Contains(outputs, o => o.Contains("_片段_001"));
        Assert.Contains(outputs, o => o.Contains("_片段_002"));
        Assert.Contains(outputs, o => o.Contains("_片段_003"));
    }

    [Fact]
    public async Task Merge_TwoInputs_BuildsConcatFilter()
    {
        var handler = new VideoMergeHandler(new FakeEngine());
        JobRequest request = Request("video.merge", new[] { "/v/a.mp4", "/v/b.mp4" });

        IReadOnlyList<string> args = handler.PreviewArguments(request);

        Assert.Contains("concat=n=2:v=1:a=1[outv][outa]", args);
        Assert.Contains("_合并", handler.PreviewOutputPaths(request)[0]);
    }

    [Fact]
    public async Task Merge_TooFewInputs_Invalid()
    {
        var handler = new VideoMergeHandler(new FakeEngine());
        JobRequest request = Request("video.merge", new[] { "/v/a.mp4" });

        ToolValidationResult result = await handler.ValidateAsync(request, default);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Geometry_Rotate90_BuildsTranspose()
    {
        var handler = new VideoGeometryHandler(new FakeEngine());
        JobRequest request = Request("video.geometry", new[] { "/v/a.mp4" },
            new() { ["rotate"] = "90", ["scaleW"] = "0" });

        IReadOnlyList<string> args = handler.PreviewArguments(request);

        Assert.Contains("transpose=1", args);
        Assert.Contains("_画面调整", handler.PreviewOutputPaths(request)[0]);
    }

    [Fact]
    public async Task SpeedVolume_BuildsSetptsAndAtempo()
    {
        var handler = new VideoSpeedVolumeHandler(new FakeEngine());
        JobRequest request = Request("video.speed-volume", new[] { "/v/a.mp4" },
            new() { ["speed"] = "2", ["volume"] = "100" });

        IReadOnlyList<string> args = handler.PreviewArguments(request);

        // The whole filter graph is one argv item, so match inside it instead of
        // comparing whole items.
        Assert.Contains(args, a => a.Contains("setpts=PTS/"));
        Assert.Contains(args, a => a.Contains("atempo="));
    }

    [Fact]
    public async Task SpeedVolume_OutOfRange_Invalid()
    {
        var handler = new VideoSpeedVolumeHandler(new FakeEngine());
        JobRequest request = Request("video.speed-volume", new[] { "/v/a.mp4" },
            new() { ["speed"] = "8", ["volume"] = "100" });

        ToolValidationResult result = await handler.ValidateAsync(request, default);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Audio_Extract_BuildsVnMp3()
    {
        var handler = new VideoAudioHandler(new FakeEngine());
        JobRequest request = Request("video.audio", new[] { "/v/a.mp4" },
            new() { ["mode"] = "extract" });

        IReadOnlyList<string> args = handler.PreviewArguments(request);

        Assert.Contains("-vn", args);
        Assert.Contains("libmp3lame", args);
        Assert.Contains("_音频", handler.PreviewOutputPaths(request)[0]);
    }

    [Fact]
    public async Task Overlay_Watermark_BuildsOverlayFilter()
    {
        var handler = new VideoOverlayHandler(new FakeEngine());
        JobRequest request = Request("video.overlay", new[] { "/v/a.mp4", "/v/logo.png" },
            new() { ["mode"] = "watermark" });

        IReadOnlyList<string> args = handler.PreviewArguments(request);

        Assert.Contains(args, a => a.Contains("overlay="));
        Assert.Contains("_水印", handler.PreviewOutputPaths(request)[0]);
    }

    [Fact]
    public async Task Frames_Sequence_BuildsNumberedPng()
    {
        var handler = new VideoFramesHandler(new FakeEngine());
        JobRequest request = Request("video.frames", new[] { "/v/a.mp4" },
            new() { ["mode"] = "sequence", ["count"] = "3", ["intervalSecs"] = "1" });

        IReadOnlyList<string> outputs = handler.PreviewOutputPaths(request);

        Assert.Contains(outputs, o => o.Contains("_帧_001.png"));
        Assert.Contains(outputs, o => o.Contains("_帧_003.png"));
    }

    [Fact]
    public async Task Metadata_Clean_BuildsMapMetadataNegative()
    {
        var handler = new VideoMetadataHandler(new FakeEngine());
        JobRequest request = Request("video.metadata", new[] { "/v/a.mp4" },
            new() { ["mode"] = "clean" });

        IReadOnlyList<string> args = handler.PreviewArguments(request);

        Assert.Contains("-map_metadata", args);
        Assert.Contains("-1", args);
        Assert.Contains("_已清理", handler.PreviewOutputPaths(request)[0]);
    }

    [Fact]
    public async Task Trim_TooShort_Invalid()
    {
        var handler = new VideoTrimHandler(new FakeEngine());
        JobRequest request = Request("video.trim", new[] { "/v/a.mp4" },
            new() { ["startSecs"] = "4", ["endSecs"] = "4" });

        ToolValidationResult result = await handler.ValidateAsync(request, default);
        Assert.False(result.IsValid);
    }
}
