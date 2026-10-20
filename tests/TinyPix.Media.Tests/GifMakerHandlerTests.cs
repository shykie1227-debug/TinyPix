using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using TinyPix.Core.Jobs;
using TinyPix.Core.Tools;
using TinyPix.Media.Ffmpeg;
using TinyPix.Media.Video;

namespace TinyPix.Media.Tests;

public sealed class GifMakerHandlerTests
{
    private sealed class FakeEngine : IFfmpegEngineProvider
    {
        public FakeEngine(string path) => FfmpegExecutablePath = path;

        public string FfmpegExecutablePath { get; }

        public string FfprobeExecutablePath => "ffprobe";
    }

    private static GifMakerHandler Handler() => new(new FakeEngine("ffmpeg"));

    [Fact]
    public void ToolId_is_frozen() => Assert.Equal("video.gif", Handler().ToolId);

    [Fact]
    public void Preview_arguments_use_paletteuse_pipeline_and_gif_output()
    {
        IReadOnlyList<string> args = Handler().PreviewArguments(
            "clip.mp4", "/out", new Dictionary<string, string>
            {
                ["format"] = "gif",
                ["fps"] = "12",
                ["width"] = "640",
                ["loopCount"] = "0",
                ["quality"] = "80",
            });

        Assert.Contains(args, a => a.Contains("paletteuse"));
        Assert.Contains("-loop", args);
        Assert.Contains("-r", args);
        Assert.Equal(Path.Combine("/out", "clip_GIF.gif"), args[^1]);
    }

    [Fact]
    public void Naming_appends_index_on_conflict()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"tinypix-gif-naming-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "clip_GIF.gif"), string.Empty);
            string path = Handler().PreviewOutputPath(
                "clip.mp4", dir, new Dictionary<string, string> { ["format"] = "gif" });
            Assert.Equal(Path.Combine(dir, "clip_GIF (1).gif"), path);
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
            JobRequest.Create("video.gif", new[] { "clip.mp4" }, "/out", JobWorkload.HeavyMedia,
                new Dictionary<string, string> { ["format"] = "mp4" }),
            CancellationToken.None);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData(0, 5)]   // valid window -> IsValid
    [InlineData(2, 40)]  // > 30s -> invalid
    public async Task Validate_enforces_duration_bounds(double start, double end)
    {
        string input = TempInput();
        try
        {
            ToolValidationResult result = await Handler().ValidateAsync(
                JobRequest.Create("video.gif", new[] { input }, "/out", JobWorkload.HeavyMedia,
                    new Dictionary<string, string>
                    {
                        ["format"] = "gif",
                        ["startSecs"] = start.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["endSecs"] = end.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    }),
                CancellationToken.None);

            Assert.Equal(end > start && (end - start) <= 30, result.IsValid);
        }
        finally
        {
            File.Delete(input);
        }
    }

    [Fact]
    public async Task Validate_rejects_end_before_start()
    {
        ToolValidationResult result = await Handler().ValidateAsync(
            JobRequest.Create("video.gif", new[] { "clip.mp4" }, "/out", JobWorkload.HeavyMedia,
                new Dictionary<string, string>
                {
                    ["format"] = "gif",
                    ["startSecs"] = "5",
                    ["endSecs"] = "2",
                }),
            CancellationToken.None);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Execute_throws_when_engine_missing()
    {
        var handler = new GifMakerHandler(new FakeEngine("/no/ffmpeg"));
        var request = JobRequest.Create("video.gif", new[] { "clip.mp4" }, "/out", JobWorkload.HeavyMedia,
            new Dictionary<string, string> { ["format"] = "gif", ["startSecs"] = "0", ["endSecs"] = "5" });

        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => handler.ExecuteAsync(
            new JobExecutionContext(request, new InlineProgress(_ => { })), CancellationToken.None));
    }

    private static string TempInput()
    {
        string path = Path.Combine(Path.GetTempPath(), $"tinypix-gif-in-{Guid.NewGuid():N}.mp4");
        File.WriteAllText(path, string.Empty);
        return path;
    }

    private sealed class InlineProgress : IProgress<JobProgress>
    {
        private readonly Action<JobProgress> _callback;

        public InlineProgress(Action<JobProgress> callback) => _callback = callback;

        public void Report(JobProgress value) => _callback(value);
    }
}
