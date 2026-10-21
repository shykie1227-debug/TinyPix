using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TinyPix.Core.Jobs;
using TinyPix.Core.Tools;
using TinyPix.Media.Capture;
using Xunit;

namespace TinyPix.Media.Tests;

public sealed class CaptureRecordHandlerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "tinypix-capture-tests", Guid.NewGuid().ToString("N"));

    public CaptureRecordHandlerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public async Task Validate_MissingH264_IsFrozenErrorBeforeQueue()
    {
        var engine = new FakeEngine { Preflight = new CapturePreflightResult(false, null) };
        ToolValidationResult validation = await new CaptureRecordHandler(engine).ValidateAsync(
            Request(new Dictionary<string, string> { ["target"] = "monitor", ["monitor"] = "0" }),
            CancellationToken.None);

        Assert.False(validation.IsValid);
        JobError error = Assert.Single(validation.Errors);
        Assert.Equal("capture.record.encoder-missing", error.Code);
        Assert.Equal(CaptureRecordHandler.MissingH264Message, error.Message);
    }

    [Fact]
    public async Task Validate_RejectsFileInputs()
    {
        var engine = new FakeEngine();
        string input = Path.Combine(_root, "a.txt");
        File.WriteAllText(input, "x");

        JobRequest request = JobRequest.Create(
            "capture.record",
            new[] { input },
            _root,
            JobWorkload.HeavyMedia,
            new Dictionary<string, string> { ["target"] = "monitor", ["monitor"] = "0" });
        ToolValidationResult validation = await new CaptureRecordHandler(engine).ValidateAsync(
            request, CancellationToken.None);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Code == "capture.record.input.unexpected");
    }

    [Theory]
    [InlineData("target=region;x=0;y=0;width=0;height=10")]
    [InlineData("target=region;x=0;y=0;width=10;height=0")]
    [InlineData("target=region;x=abc;y=0;width=10;height=10")]
    [InlineData("target=region;width=10;height=10")]
    [InlineData("target=window;hwnd=0")]
    [InlineData("target=window;hwnd=abc")]
    [InlineData("target=window")]
    [InlineData("target=monitor;monitor=-1")]
    [InlineData("target=monitor")]
    [InlineData("target=bogus;x=0;y=0;width=10;height=10")]
    public async Task Validate_BadTarget_IsRejected(string serialized)
    {
        var engine = new FakeEngine();
        var parameters = new Dictionary<string, string>();
        foreach (string pair in serialized.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split('=', 2);
            parameters[parts[0]] = parts[1];
        }

        ToolValidationResult validation = await new CaptureRecordHandler(engine).ValidateAsync(
            Request(parameters), CancellationToken.None);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Code == "capture.record.target-invalid");
    }

    [Fact]
    public async Task Validate_GoodRegion_PassesWithAvailableEngine()
    {
        var engine = new FakeEngine();
        ToolValidationResult validation = await new CaptureRecordHandler(engine).ValidateAsync(
            Request(new Dictionary<string, string>
            {
                ["target"] = "region",
                ["x"] = "10",
                ["y"] = "20",
                ["width"] = "640",
                ["height"] = "480",
            }),
            CancellationToken.None);

        Assert.True(validation.IsValid);
    }

    [Fact]
    public void ParseProfile_AudioFlagsAreOptInAndQualityStandardOnly()
    {
        ScreenCaptureProfile quiet = CaptureRecordHandler.ParseProfile(new Dictionary<string, string>());
        Assert.False(quiet.SystemAudio);
        Assert.False(quiet.Microphone);
        Assert.Equal(1920, quiet.MaxWidth);
        Assert.Equal(1080, quiet.MaxHeight);
        Assert.Equal(30, quiet.FrameRate);
        Assert.Equal(8_000_000, quiet.VideoBitrate);
        Assert.Equal(128_000, quiet.AudioBitrate);
        Assert.Equal(48_000, quiet.AudioSampleRate);
        Assert.True(quiet.IncludeCursor);

        ScreenCaptureProfile both = CaptureRecordHandler.ParseProfile(new Dictionary<string, string>
        {
            ["systemAudio"] = "1",
            ["microphone"] = "true",
            ["quality"] = "standard",
        });
        Assert.True(both.SystemAudio);
        Assert.True(both.Microphone);

        Assert.Throws<InvalidOperationException>(() => CaptureRecordHandler.ParseProfile(
            new Dictionary<string, string> { ["quality"] = "ultra" }));
    }

    [Fact]
    public async Task Execute_UserStop_FinalizesOutputAsSuccess()
    {
        var session = new FakeSession(Path.Combine(_root, "录屏_test.mp4"));
        var engine = new FakeEngine { Session = session };
        var handler = new CaptureRecordHandler(engine);
        using var cancellation = new CancellationTokenSource();

        Task<ToolExecutionResult> run = handler.ExecuteAsync(
            new JobExecutionContext(
                Request(new Dictionary<string, string> { ["target"] = "monitor", ["monitor"] = "0" }),
                new NullProgress()),
            cancellation.Token);
        await Task.Delay(300);
        cancellation.Cancel(); // 用户"停止"→队列 TryCancel 的语义

        ToolExecutionResult result = await run;
        Assert.Null(result.PartialFailure);
        Assert.Equal(session.OutputPath, Assert.Single(result.OutputPaths));
        Assert.True(session.Stopped);
    }

    [Fact]
    public void GeneratedPath_UsesFrozenNameAndConflictSuffix()
    {
        var timestamp = new DateTime(2026, 10, 20, 15, 30, 45);
        string first = CaptureOutputNaming.BuildGeneratedPath(timestamp, _root);
        Assert.Equal("录屏_20261020_153045.mp4", Path.GetFileName(first));

        File.WriteAllBytes(first, new byte[] { 1 });
        string second = CaptureOutputNaming.BuildGeneratedPath(timestamp, _root);
        Assert.Equal("录屏_20261020_153045 (1).mp4", Path.GetFileName(second));
    }

    [Fact]
    public void AudioSourceLabel_MatchesFrozenWording()
    {
        Assert.Equal("无", CaptureOutputNaming.AudioSourceLabel(false, false));
        Assert.Equal("系统音频", CaptureOutputNaming.AudioSourceLabel(true, false));
        Assert.Equal("麦克风", CaptureOutputNaming.AudioSourceLabel(false, true));
        Assert.Equal("系统音频+麦克风", CaptureOutputNaming.AudioSourceLabel(true, true));
    }

    [Theory]
    [InlineData("捕获权限被拒绝", "capture.record.permission-denied")]
    [InlineData("The device was lost", "capture.record.device-lost")]
    [InlineData("磁盘空间不足", "capture.record.disk-full")]
    [InlineData("something else", "capture.record.failed")]
    public void MapEngineError_ClassifiesFrozenCodes(string message, string expectedCode)
    {
        JobError error = CaptureRecordHandler.MapEngineError(
            new InvalidOperationException(message), Path.Combine(_root, "out.mp4"));
        Assert.Equal(expectedCode, error.Code);
    }

    [Fact]
    public async Task Execute_EngineFailure_ReportsPartialWithMappedCode()
    {
        var session = new FakeSession(Path.Combine(_root, "录屏_fail.mp4"))
        {
            StopException = new InvalidOperationException("捕获权限被拒绝"),
        };
        var engine = new FakeEngine { Session = session };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));

        ToolExecutionResult result = await new CaptureRecordHandler(engine).ExecuteAsync(
            new JobExecutionContext(
                Request(new Dictionary<string, string> { ["target"] = "window", ["hwnd"] = "12345" }),
                new NullProgress()),
            cancellation.Token);

        Assert.NotNull(result.PartialFailure);
        Assert.Equal("capture.record.permission-denied", result.PartialFailure!.Code);
        Assert.Empty(result.OutputPaths);
    }

    [Fact]
    public async Task Estimate_IsUnknowableUpfront()
    {
        ToolEstimate estimate = await new CaptureRecordHandler(new FakeEngine()).EstimateAsync(
            Request(new Dictionary<string, string>()), CancellationToken.None);
        Assert.Null(estimate.Duration);
        Assert.Null(estimate.OutputBytes);
    }

    private JobRequest Request(IReadOnlyDictionary<string, string> parameters) =>
        JobRequest.Create(
            "capture.record",
            Array.Empty<string>(),
            _root,
            JobWorkload.HeavyMedia,
            new Dictionary<string, string>(parameters));

    private sealed class FakeEngine : IScreenCaptureEngine
    {
        public CapturePreflightResult Preflight { get; set; } = CapturePreflightResult.Available();
        public FakeSession? Session { get; set; }

        public Task<CapturePreflightResult> PreflightAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Preflight);

        public Task<IScreenCaptureSession> StartAsync(
            ScreenCaptureTarget target,
            ScreenCaptureProfile profile,
            string outputPath,
            CancellationToken cancellationToken) =>
            Task.FromResult<IScreenCaptureSession>(Session ?? new FakeSession(outputPath));
    }

    private sealed class FakeSession : IScreenCaptureSession
    {
        private readonly DateTime _started = DateTime.UtcNow;

        public FakeSession(string outputPath) => OutputPath = outputPath;

        public string OutputPath { get; }
        public TimeSpan Elapsed => DateTime.UtcNow - _started;
        public bool Stopped { get; private set; }
        public Exception? StopException { get; set; }

        public Task<CaptureSessionResult> StopAsync(CancellationToken cancellationToken)
        {
            Stopped = true;
            if (StopException is not null)
            {
                throw StopException;
            }

            return Task.FromResult(new CaptureSessionResult(
                OutputPath, TimeSpan.FromSeconds(2.5), 1920, 1080, 30, "无"));
        }

        public void Dispose() { }
    }

    private sealed class NullProgress : IProgress<JobProgress>
    {
        public void Report(JobProgress value) { }
    }
}
