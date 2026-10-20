using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TinyPix.Core.Jobs;
using TinyPix.Core.Tools;

namespace TinyPix.Media.Capture;

/// <summary>
/// 屏幕录制 (capture.record). Frozen spec (FEATURE-MATRIX §8):
/// targets 屏幕/窗口/框选区域 + 本机系统音频/麦克风; "标准质量" 1920×1080 上限、
/// 30 fps、H.264 8 Mbps、AAC 128 kbps/48 kHz; 超大区域等比缩至 1080p 不放大;
/// 音频默认关闭、含光标; 倒计时 3 秒（UI 侧）; 输出 <c>录屏_YYYYMMDD_HHMMSS.mp4</c>;
/// 一次一个目标, 无定时批量/多轨; 成功显示时长/尺寸/帧率/音频来源。
/// Stop semantics: 用户"停止"→队列 TryCancel→token 触发→本 handler 优雅收尾
/// 并返回已录制输出（不抛出 OperationCanceledException, 否则队列按"取消"丢弃输出）。
/// </summary>
public sealed class CaptureRecordHandler : IToolHandler
{
    /// <summary>E1 冻结报错文案（FEATURE-MATRIX §2.1）。</summary>
    public const string MissingH264Message = "此电脑缺少可用的 Windows H.264 编码器";

    private readonly IScreenCaptureEngine _engine;

    public CaptureRecordHandler(IScreenCaptureEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public string ToolId => "capture.record";

    public async ValueTask<ToolValidationResult> ValidateAsync(
        JobRequest request,
        CancellationToken cancellationToken)
    {
        var errors = new List<JobError>();

        // 任务入队前探测 H.264 编码器（冻结门禁）。
        CapturePreflightResult preflight = await _engine
            .PreflightAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!preflight.H264Available)
        {
            errors.Add(new JobError(
                "capture.record.encoder-missing",
                string.IsNullOrWhiteSpace(preflight.MissingReason)
                    ? MissingH264Message
                    : preflight.MissingReason!,
                true));
        }

        // 一次一个目标（supportsBatch=false）：不接受文件输入。
        if (request.InputPaths.Count > 0)
        {
            errors.Add(new JobError(
                "capture.record.input.unexpected",
                "屏幕录制不接受文件输入，请选择屏幕、窗口或框选区域。",
                false));
        }

        ScreenCaptureTarget? target = ParseTarget(request.Parameters, out string? targetError);
        if (target is null && targetError is not null)
        {
            errors.Add(new JobError("capture.record.target-invalid", targetError, false));
        }

        return errors.Count == 0
            ? ToolValidationResult.Valid()
            : ToolValidationResult.Invalid(errors.ToArray());
    }

    public ValueTask<ToolEstimate> EstimateAsync(
        JobRequest request,
        CancellationToken cancellationToken)
    {
        // 录制时长由用户停止时机决定, 事前不可估。
        return ValueTask.FromResult(new ToolEstimate(null, null));
    }

    public async Task<ToolExecutionResult> ExecuteAsync(
        JobExecutionContext context,
        CancellationToken cancellationToken)
    {
        JobRequest request = context.Request;
        ScreenCaptureTarget target = ParseTarget(request.Parameters, out string? targetError)
            ?? throw new InvalidOperationException(targetError ?? "无效的录制目标。");
        ScreenCaptureProfile profile = ParseProfile(request.Parameters);

        Directory.CreateDirectory(request.OutputDirectory);
        string outputPath = CaptureOutputNaming.BuildGeneratedPath(
            DateTime.Now, request.OutputDirectory);

        IScreenCaptureSession session = await _engine
            .StartAsync(target, profile, outputPath, cancellationToken)
            .ConfigureAwait(false);

        using (session)
        {
            // 录制中: 显示时长。停止 = 用户取消; 崩溃/设备丢失 = 引擎侧异常。
            try
            {
                using var pollCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                while (!pollCancellation.IsCancellationRequested)
                {
                    TimeSpan elapsed = session.Elapsed;
                    context.Progress.Report(new JobProgress(
                        0,
                        $"录制中 {FormatElapsed(elapsed)}"));
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), pollCancellation.Token)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                // 优雅停止: flush 编码器并完成 MP4; 之后按普通成功输出。
                CaptureSessionResult result = await session
                    .StopAsync(CancellationToken.None)
                    .ConfigureAwait(false);
                context.Progress.Report(new JobProgress(
                    100,
                    $"录制完成 {FormatElapsed(result.Duration)} · {result.Width}×{result.Height} · {result.FrameRate} fps · {result.AudioSource}"));
                return ToolExecutionResult.Success(new[] { result.OutputPath });
            }
            catch (Exception exception) when (
                exception is not OperationCanceledException)
            {
                // 设备丢失/权限拒绝/磁盘满等由引擎抛出; 映射为冻结错误码。
                return ToolExecutionResult.Partial(
                    Array.Empty<string>(),
                    MapEngineError(exception, session.OutputPath));
            }
        }
    }

    public static JobError MapEngineError(Exception exception, string outputPath)
    {
        string message = exception.Message;
        if (message.Contains("权限", StringComparison.Ordinal) ||
            message.Contains("拒绝", StringComparison.Ordinal) ||
            message.Contains("access", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("denied", StringComparison.OrdinalIgnoreCase))
        {
            return new JobError("capture.record.permission-denied", message, true);
        }

        if (message.Contains("空间", StringComparison.Ordinal) ||
            message.Contains("disk", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("full", StringComparison.OrdinalIgnoreCase))
        {
            return new JobError("capture.record.disk-full", message, true);
        }

        if (message.Contains("设备", StringComparison.Ordinal) ||
            message.Contains("device", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("lost", StringComparison.OrdinalIgnoreCase))
        {
            return new JobError("capture.record.device-lost", message, true);
        }

        return new JobError("capture.record.failed", message, true);
    }

    /// <summary>解析录制目标。参数缺失时默认框选（冻结默认"默认区域"）。</summary>
    public static ScreenCaptureTarget? ParseTarget(
        IReadOnlyDictionary<string, string> parameters,
        out string? error)
    {
        error = null;
        string kind = Get(parameters, "target").ToLowerInvariant();
        switch (kind)
        {
            case "":
            case "region":
            {
                if (!TryInt(parameters, "x", out int x) ||
                    !TryInt(parameters, "y", out int y) ||
                    !TryInt(parameters, "width", out int width) ||
                    !TryInt(parameters, "height", out int height))
                {
                    error = "框选区域需要 x/y/width/height 四个整数。";
                    return null;
                }

                if (width <= 0 || height <= 0)
                {
                    error = "框选区域的宽高必须大于 0。";
                    return null;
                }

                return new RegionCaptureTarget(x, y, width, height);
            }

            case "window":
            {
                if (!TryLong(parameters, "hwnd", out long hwnd) || hwnd == 0)
                {
                    error = "窗口捕获需要有效的窗口句柄 (hwnd)。";
                    return null;
                }

                return new WindowCaptureTarget(hwnd);
            }

            case "monitor":
            {
                if (!TryInt(parameters, "monitor", out int monitor) || monitor < 0)
                {
                    error = "屏幕捕获需要非负的显示器序号 (monitor)。";
                    return null;
                }

                return new MonitorCaptureTarget(monitor);
            }

            default:
                error = $"未知的录制目标类型：{kind}（支持 region/window/monitor）。";
                return null;
        }
    }

    /// <summary>解析画质档: 目前只有冻结的"标准质量", 其余取值一律拒绝。</summary>
    public static ScreenCaptureProfile ParseProfile(
        IReadOnlyDictionary<string, string> parameters)
    {
        string quality = Get(parameters, "quality").ToLowerInvariant();
        if (quality.Length > 0 && quality != "standard")
        {
            throw new InvalidOperationException(
                $"未知画质档：{quality}（当前仅支持 standard=标准质量）。");
        }

        return ScreenCaptureProfile.Standard with
        {
            SystemAudio = TryBool(parameters, "systemAudio"),
            Microphone = TryBool(parameters, "microphone"),
        };
    }

    private static string FormatElapsed(TimeSpan elapsed) =>
        elapsed.TotalHours >= 1
            ? elapsed.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : elapsed.ToString(@"mm\:ss", CultureInfo.InvariantCulture);

    private static string Get(IReadOnlyDictionary<string, string> parameters, string key) =>
        parameters.TryGetValue(key, out string? value) && value is not null ? value.Trim() : string.Empty;

    private static bool TryBool(IReadOnlyDictionary<string, string> parameters, string key) =>
        Get(parameters, key) is "1" or "true" or "True" or "yes";

    private static bool TryInt(IReadOnlyDictionary<string, string> parameters, string key, out int value) =>
        int.TryParse(Get(parameters, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static bool TryLong(IReadOnlyDictionary<string, string> parameters, string key, out long value) =>
        long.TryParse(Get(parameters, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
}
