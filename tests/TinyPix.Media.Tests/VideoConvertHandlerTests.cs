using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using TinyPix.Core.Jobs;
using TinyPix.Media.Ffmpeg;
using TinyPix.Media.Video;

namespace TinyPix.Media.Tests;

public sealed class VideoConvertHandlerTests
{
    private sealed class FakeEngine : IFfmpegEngineProvider
    {
        public FakeEngine(string path) => FfmpegExecutablePath = path;

        public string FfmpegExecutablePath { get; }
    }

    private static VideoConvertHandler Handler() => new(new FakeEngine("ffmpeg"));

    [Fact]
    public void Mp4_maps_quality_80_to_crf_20_with_h264_aac()
    {
        IReadOnlyList<string> args = Handler().PreviewArguments(
            "clip.mp4", "/out", new Dictionary<string, string> { ["format"] = "mp4", ["quality"] = "80" });

        int codec = args.ToList().IndexOf("-c:v");
        Assert.Equal("libx264", args[codec + 1]);
        int crf = args.ToList().IndexOf("-crf");
        Assert.Equal("20", args[crf + 1]);
        int audio = args.ToList().IndexOf("-c:a");
        Assert.Equal("aac", args[audio + 1]);
        Assert.Equal("/out/clip_转换.mp4", args[^1]);
    }

    [Theory]
    [InlineData(100, 18)]
    [InlineData(80, 20)]
    [InlineData(0, 28)]
    public void Quality_maps_to_clamped_crf(int quality, int crf)
    {
        IReadOnlyList<string> args = Handler().PreviewArguments(
            "clip.mp4", "/out", new Dictionary<string, string> { ["format"] = "mp4", ["quality"] = quality.ToString() });

        int index = args.ToList().IndexOf("-crf");
        Assert.Equal(crf.ToString(), args[index + 1]);
    }

    [Fact]
    public void Audio_only_mp3_extracts_with_vn_and_libmp3lame()
    {
        IReadOnlyList<string> args = Handler().PreviewArguments(
            "clip.mp4", "/out", new Dictionary<string, string> { ["format"] = "mp3" });

        Assert.Contains("-vn", args);
        int audio = args.ToList().IndexOf("-c:a");
        Assert.Equal("libmp3lame", args[audio + 1]);
        Assert.Equal("/out/clip_转换.mp3", args[^1]);
    }

    [Fact]
    public void Webm_uses_libvpx_with_bv_0_and_libvorbis()
    {
        IReadOnlyList<string> args = Handler().PreviewArguments(
            "clip.mp4", "/out", new Dictionary<string, string> { ["format"] = "webm" });

        int codec = args.ToList().IndexOf("-c:v");
        Assert.Equal("libvpx", args[codec + 1]);
        int bv = args.ToList().IndexOf("-b:v");
        Assert.Equal("0", args[bv + 1]);
        int audio = args.ToList().IndexOf("-c:a");
        Assert.Equal("libvorbis", args[audio + 1]);
    }

    [Fact]
    public void Naming_appends_index_on_conflict()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"tinypix-naming-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "clip_转换.mp4"), string.Empty);
            string path = Handler().PreviewOutputPath(
                "clip.mp4", dir, new Dictionary<string, string> { ["format"] = "mp4" });
            Assert.Equal(Path.Combine(dir, "clip_转换 (1).mp4"), path);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Validate_rejects_unsupported_format()
    {
        ToolValidationResult result = await Handler().ValidateAsync(
            JobRequest.Create("video.convert", new[] { "clip.mp4" }, "/out", JobWorkload.HeavyMedia,
                new Dictionary<string, string> { ["format"] = "docx" }),
            CancellationToken.None);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Validate_rejects_missing_input_file()
    {
        ToolValidationResult result = await Handler().ValidateAsync(
            JobRequest.Create("video.convert", new[] { "/nope/clip.mp4" }, "/out", JobWorkload.HeavyMedia,
                new Dictionary<string, string> { ["format"] = "mp4" }),
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "video.input.missing");
    }

    [Fact]
    public async Task Validate_rejects_out_of_range_quality()
    {
        ToolValidationResult result = await Handler().ValidateAsync(
            JobRequest.Create("video.convert", new[] { "clip.mp4" }, "/out", JobWorkload.HeavyMedia,
                new Dictionary<string, string> { ["format"] = "mp4", ["quality"] = "150" }),
            CancellationToken.None);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Execute_throws_when_engine_missing()
    {
        var handler = new VideoConvertHandler(new FakeEngine("/no/ffmpeg"));
        var request = JobRequest.Create("video.convert", new[] { "clip.mp4" }, "/out", JobWorkload.HeavyMedia,
            new Dictionary<string, string> { ["format"] = "mp4" });

        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => handler.ExecuteAsync(
            new JobExecutionContext(request, new InlineProgress(_ => { })), CancellationToken.None));
    }

    private sealed class InlineProgress : IProgress<JobProgress>
    {
        private readonly Action<JobProgress> _callback;

        public InlineProgress(Action<JobProgress> callback) => _callback = callback;

        public void Report(JobProgress value) => _callback(value);
    }
}
