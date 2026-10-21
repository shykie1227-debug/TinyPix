using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TinyPix.Media.Capture;

/// <summary>
/// 屏幕录制目标（FEATURE-MATRIX §8 capture.record）：屏幕、窗口、框选区域三选一。
/// 派发时由 UI 把选择解析为具体目标；引擎只消费已解析目标。
/// </summary>
public abstract record ScreenCaptureTarget;

/// <summary>整块显示器捕获（capture.screenshot/record 的"屏幕"来源）。</summary>
public sealed record MonitorCaptureTarget(int MonitorIndex) : ScreenCaptureTarget;

/// <summary>指定窗口捕获（HWND 由 UI 侧拾取后传入）。</summary>
public sealed record WindowCaptureTarget(long WindowHandle) : ScreenCaptureTarget;

/// <summary>框选区域捕获（Windows 逻辑坐标；引擎映射到实际像素）。</summary>
public sealed record RegionCaptureTarget(int X, int Y, int Width, int Height) : ScreenCaptureTarget;

/// <summary>
/// 录屏画质档。冻结默认"标准质量"（FEATURE-MATRIX §8）：
/// 1920×1080 上限、30 fps、H.264 目标 8 Mbps、AAC 128 kbps/48 kHz；
/// 超大区域等比缩至 1080p 且不放大；系统音频/麦克风默认关闭；含光标。
/// </summary>
public sealed record ScreenCaptureProfile(
    int MaxWidth = 1920,
    int MaxHeight = 1080,
    int FrameRate = 30,
    long VideoBitrate = 8_000_000,
    long AudioBitrate = 128_000,
    int AudioSampleRate = 48_000,
    bool SystemAudio = false,
    bool Microphone = false,
    bool IncludeCursor = true)
{
    public static ScreenCaptureProfile Standard { get; } = new();
}

/// <summary>预检结果：E1 Media Foundation H.264 编码器可用性。</summary>
public sealed record CapturePreflightResult(bool H264Available, string? MissingReason)
{
    public static CapturePreflightResult Available() => new(true, null);
}

/// <summary>一次录制结束后的冻结成功状态字段（时长、尺寸、帧率、音频来源）。</summary>
public sealed record CaptureSessionResult(
    string OutputPath,
    TimeSpan Duration,
    int Width,
    int Height,
    int FrameRate,
    string AudioSource);

/// <summary>
/// 录制会话。UI 的"停止"映射为调用 <see cref="StopAsync"/>；引擎必须优雅收尾
/// （flush 编码器、写完 MP4 moov），崩溃遗留只允许出现在 Cache/Temp。
/// </summary>
public interface IScreenCaptureSession : IDisposable
{
    string OutputPath { get; }

    /// <summary>已录制时长（供"录制中显示时长"轮询）。</summary>
    TimeSpan Elapsed { get; }

    /// <summary>停止并完成输出文件；重复调用返回同一结果。</summary>
    Task<CaptureSessionResult> StopAsync(CancellationToken cancellationToken);
}

/// <summary>
/// 录屏引擎抽象（E11 Windows Graphics Capture + E1 Media Foundation）。
/// 唯一实现 <c>TinyPix.Capture.WindowsScreenCaptureEngine</c> 落在 Windows 专用
/// 类库；Media.Tests 用 FakeEngine 覆盖 handler 逻辑。
/// </summary>
public interface IScreenCaptureEngine
{
    /// <summary>任务入队前探测 H.264 编码器（缺失时冻结文案报错，禁用开始）。</summary>
    Task<CapturePreflightResult> PreflightAsync(CancellationToken cancellationToken);

    /// <summary>启动录制到 <paramref name="outputPath"/>（新文件，不覆盖）。</summary>
    Task<IScreenCaptureSession> StartAsync(
        ScreenCaptureTarget target,
        ScreenCaptureProfile profile,
        string outputPath,
        CancellationToken cancellationToken);
}

/// <summary>capture.record 冻结命名 <c>录屏_YYYYMMDD_HHMMSS.mp4</c> 与冲突 <c> (n)</c> 规则。</summary>
public static class CaptureOutputNaming
{
    public const string Prefix = "录屏_";

    public static string BuildGeneratedPath(DateTime timestamp, string outputDirectory)
    {
        string name = $"{Prefix}{timestamp:yyyyMMdd_HHmmss}.mp4";
        string candidate = Path.Combine(outputDirectory, name);
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        int index = 1;
        string conflict;
        do
        {
            conflict = Path.Combine(
                outputDirectory,
                $"{Prefix}{timestamp:yyyyMMdd_HHmmss} ({index}).mp4");
            index++;
        }
        while (File.Exists(conflict));

        return conflict;
    }

    /// <summary>音频来源的冻结显示文案。</summary>
    public static string AudioSourceLabel(bool systemAudio, bool microphone) =>
        (systemAudio, microphone) switch
        {
            (true, true) => "系统音频+麦克风",
            (true, false) => "系统音频",
            (false, true) => "麦克风",
            _ => "无",
        };
}
