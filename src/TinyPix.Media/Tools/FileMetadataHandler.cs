using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ImageMagick;
using TinyPix.Core.Jobs;
using TinyPix.Core.Tools;
using TinyPix.Media.Ffmpeg;
using TinyPix.Media.Images;
using TinyPix.Media.Pdf;
using TinyPix.Media.Video;
using UglyToad.PdfPig;
using PdfDocumentOpenMode = PdfSharp.Pdf.IO.PdfDocumentOpenMode;
using PdfReader = PdfSharp.Pdf.IO.PdfReader;
using PdfSharpDocument = PdfSharp.Pdf.PdfDocument;

namespace TinyPix.Media.Tools;

/// <summary>
/// 文件与媒体信息 (file.metadata). Frozen defaults (FEATURE-MATRIX §8):
/// <list type="bullet">
///   <item><description>view（默认）— 只读：基础信息 + 图片(E3)/音视频(E2 ffprobe)/PDF(E8) 深度字段；不产生输出</description></item>
///   <item><description>export=1 — 在 view 基础上按输入文件各写一份 <c>{原名}_信息.txt</c></description></item>
///   <item><description>clean — 仅对 E2/E3/E8 明确支持字段开放，输出 <c>{原名}_已清理.{原扩展名}</c>；签名 PDF 硬阻止</description></item>
/// </list>
/// 源文件严格只读；清理只写新副本。不承诺正文/画面隐私清除（冻结口径）。
/// </summary>
public sealed class FileMetadataHandler : IToolHandler
{
    // 深度解析矩阵（FEATURE-MATRIX §8）：只有这些类型进入 E2/E3/E8 分支，
    // 其余文件只给基础信息，clean 模式逐项报“不支持字段”。
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif", ".tif", ".tiff",
    };

    private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".mkv", ".avi", ".webm", ".flv", ".wmv", ".m4v", ".ts",
        ".mp3", ".wav", ".flac", ".aac", ".m4a", ".ogg", ".opus", ".wma",
    };

    private static readonly HashSet<string> PdfExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf",
    };

    private readonly IFfmpegEngineProvider _engine;

    public FileMetadataHandler(IFfmpegEngineProvider engine) => _engine = engine;

    public string ToolId => "file.metadata";

    public ValueTask<ToolValidationResult> ValidateAsync(
        JobRequest request,
        CancellationToken cancellationToken)
    {
        var errors = new List<JobError>();

        if (request.InputPaths.Count == 0)
        {
            errors.Add(new JobError("file.metadata.input.empty", "请至少添加一个文件。", false));
        }

        string mode = Mode(request);
        if (mode is not ("view" or "clean"))
        {
            errors.Add(new JobError(
                "file.metadata.mode-unsupported", $"不支持的模式：{mode}（支持 view / clean）", false));
        }

        foreach (string input in request.InputPaths)
        {
            if (Directory.Exists(input))
            {
                errors.Add(new JobError(
                    "file.metadata.input.folder", $"不支持文件夹输入：{input}", false));
                continue;
            }

            if (!File.Exists(input))
            {
                errors.Add(new JobError("file.metadata.input.missing", $"文件不存在：{input}", false));
                continue;
            }

            // 签名 PDF 清理硬阻止（冻结规则）：入队前拦截，不提供“仍然继续”。
            if (mode == "clean" && IsPdf(input))
            {
                string? signatureProblem = PdfRules.CheckCleanAllowed(input);
                if (signatureProblem is not null)
                {
                    errors.Add(new JobError("file.metadata.signed-pdf", signatureProblem, false));
                }
            }
        }

        return errors.Count == 0
            ? ValueTask.FromResult(ToolValidationResult.Valid())
            : ValueTask.FromResult(ToolValidationResult.Invalid(errors.ToArray()));
    }

    public ValueTask<ToolEstimate> EstimateAsync(
        JobRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(new ToolEstimate(null, null));

    public async Task<ToolExecutionResult> ExecuteAsync(
        JobExecutionContext context,
        CancellationToken cancellationToken)
    {
        string mode = Mode(context.Request);
        bool export = Flag(context.Request.Parameters, "export");

        Directory.CreateDirectory(context.Request.OutputDirectory);

        var outputs = new List<string>();
        var failures = new List<JobError>();
        double done = 0;

        foreach (string input in context.Request.InputPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (mode == "clean")
                {
                    string cleaned = CleanOne(input, context.Request, cancellationToken);
                    outputs.Add(cleaned);

                    if (export)
                    {
                        outputs.Add(ExportOne(input, context.Request, new[] { $"清理：已写入 {Path.GetFileName(cleaned)}" }));
                    }
                }
                else
                {
                    IReadOnlyList<(string Group, string Name, string Value)> rows =
                        ReadSummary(input, _engine, cancellationToken);

                    if (export)
                    {
                        outputs.Add(ExportOne(input, context.Request, Array.Empty<string>()));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                failures.Add(new JobError(
                    "file.metadata.process-failed",
                    $"{Path.GetFileName(input)} 处理失败：{error.Message}",
                    true));
            }

            done++;
            context.Progress.Report(new JobProgress(
                done / context.Request.InputPaths.Count * 100,
                mode == "clean" ? "正在清理元数据" : "正在读取文件信息"));
        }

        if (outputs.Count == 0 && failures.Count > 0)
        {
            throw new InvalidOperationException(failures[0].Message);
        }

        return failures.Count == 0
            ? ToolExecutionResult.Success(outputs)
            : ToolExecutionResult.Partial(outputs, failures[0]);
    }

    /// <summary>
    /// Read-only field summary used by the UI panel (结构化字段和前后对照) and by
    /// the tests. Never writes anything. <paramref name="engine"/> is only touched
    /// for E2 media files; pass any provider for other kinds.
    /// </summary>
    public static IReadOnlyList<(string Group, string Name, string Value)> ReadSummary(
        string path,
        IFfmpegEngineProvider engine,
        CancellationToken cancellationToken = default)
    {
        var rows = new List<(string Group, string Name, string Value)>();
        var info = new FileInfo(path);

        rows.Add(("基础", "文件名", info.Name));
        rows.Add(("基础", "路径", info.FullName));
        rows.Add(("基础", "大小", $"{info.Length.ToString("N0", CultureInfo.InvariantCulture)} 字节（{HumanSize(info.Length)}）"));
        rows.Add(("基础", "创建时间", info.CreationTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
        rows.Add(("基础", "修改时间", info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));

        if (ImageExtensions.Contains(info.Extension))
        {
            rows.AddRange(ReadImageRows(path));
        }
        else if (MediaExtensions.Contains(info.Extension))
        {
            rows.AddRange(ReadMediaRows(path, engine, cancellationToken));
        }
        else if (PdfExtensions.Contains(info.Extension))
        {
            rows.AddRange(PdfRules.ReadRows(path));
        }
        else
        {
            rows.Add(("深度解析", "支持范围", "不支持（仅基础信息；深度矩阵：图片/音视频/PDF）"));
        }

        return rows;
    }

    private static IReadOnlyList<(string, string, string)> ReadImageRows(string path)
    {
        var rows = new List<(string, string, string)>();
        using var image = new MagickImage(path);
        rows.Add(("图片", "格式", image.Format.ToString()));
        rows.Add(("图片", "尺寸", $"{image.Width}×{image.Height}"));
        rows.Add(("图片", "色彩空间", image.ColorSpace.ToString()));
        rows.Add(("图片", "位深", image.Depth.ToString(CultureInfo.InvariantCulture)));

        IExifProfile? exif = image.GetExifProfile();
        int count = exif?.Values.Count(v => v is not null) ?? 0;
        rows.Add(("图片", "EXIF", count == 0 ? "无" : $"{count} 个字段"));
        return rows;
    }

    private static IReadOnlyList<(string, string, string)> ReadMediaRows(
        string path,
        IFfmpegEngineProvider engine,
        CancellationToken cancellationToken)
    {
        var rows = new List<(string, string, string)>();
        FfprobeResult probe = new FfprobeRunner().RunAsync(
            engine.FfprobeExecutablePath, path, cancellationToken).GetAwaiter().GetResult();

        if (probe.ExitCode != 0 || string.IsNullOrWhiteSpace(probe.StandardOutput))
        {
            rows.Add(("音视频", "解析", $"ffprobe 失败（退出码 {probe.ExitCode}）"));
            return rows;
        }

        using JsonDocument document = JsonDocument.Parse(probe.StandardOutput);
        JsonElement root = document.RootElement;

        if (root.TryGetProperty("format", out JsonElement format))
        {
            rows.Add(("音视频", "容器", Text(format, "format_name") ?? "未知"));
            rows.Add(("音视频", "时长", FormatDuration(Text(format, "duration"))));
            long bitRate = Number(format, "bit_rate");
            if (bitRate > 0)
            {
                rows.Add(("音视频", "总码率", $"{bitRate / 1000.0:0.#} kbps"));
            }
        }

        if (root.TryGetProperty("streams", out JsonElement streams))
        {
            JsonElement video = FirstOfType(streams, "video");
            if (video.ValueKind == JsonValueKind.Object)
            {
                rows.Add(("音视频", "视频编码", Text(video, "codec_name") ?? "未知"));
                rows.Add(("音视频", "画面尺寸", $"{Number(video, "width")}×{Number(video, "height")}"));
                rows.Add(("音视频", "帧率", FormatRate(Text(video, "avg_frame_rate"))));
            }

            JsonElement audio = FirstOfType(streams, "audio");
            if (audio.ValueKind == JsonValueKind.Object)
            {
                rows.Add(("音视频", "音频编码", Text(audio, "codec_name") ?? "未知"));
                rows.Add(("音视频", "采样率", $"{Number(audio, "sample_rate")} Hz"));
                rows.Add(("音视频", "声道", Number(audio, "channels").ToString(CultureInfo.InvariantCulture)));
            }

            if (video.ValueKind != JsonValueKind.Object && audio.ValueKind != JsonValueKind.Object)
            {
                rows.Add(("音视频", "流", "未识别到音视频流"));
            }
        }

        return rows;
    }

    private string CleanOne(
        string input,
        JobRequest request,
        CancellationToken cancellationToken)
    {
        string extension = Path.GetExtension(input);
        string output = VideoOutputNaming.BuildOutputPathByExtension(
            input, "_已清理", extension.TrimStart('.'), request.OutputDirectory);

        if (ImageExtensions.Contains(extension))
        {
            using var image = new MagickImage(input);
            ImageExifHandler.StripMetadata(image);
            image.Write(output);
            return output;
        }

        if (MediaExtensions.Contains(extension))
        {
            string enginePath = _engine.FfmpegExecutablePath;
            if (string.IsNullOrWhiteSpace(enginePath) || !File.Exists(enginePath))
            {
                throw new InvalidOperationException($"无法定位本地媒体引擎：{enginePath ?? "(空)"}");
            }

            // 与 video.metadata clean 同一冻结参数：剥离全部容器元数据、流原样拷贝。
            FfmpegProcessResult result = new FfmpegProcessRunner().RunAsync(
                enginePath,
                new[] { "-y", "-i", input, "-map_metadata", "-1", "-c", "copy", output },
                duration: null,
                progress: null,
                cancellationToken).GetAwaiter().GetResult();

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"媒体引擎返回非零退出码 {result.ExitCode}。{Truncate(result.StandardError)}");
            }

            return output;
        }

        if (PdfExtensions.Contains(extension))
        {
            PdfRules.Clean(input, output);
            return output;
        }

        throw new InvalidOperationException(
            $"不支持清理该类型的元数据（深度矩阵仅开放 图片/音视频/PDF）：{Path.GetFileName(input)}");
    }

    private string ExportOne(string input, JobRequest request, IReadOnlyList<string> notes)
    {
        string report = Path.Combine(
            request.OutputDirectory,
            $"{Path.GetFileNameWithoutExtension(input)}_信息.txt");

        IReadOnlyList<(string Group, string Name, string Value)> rows =
            ReadSummary(input, _engine, CancellationToken.None);

        var builder = new StringBuilder();
        builder.AppendLine("TinyPix 文件与媒体信息");
        builder.AppendLine($"生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine();
        builder.AppendLine($"文件：{Path.GetFileName(input)}");
        builder.AppendLine();

        string? group = null;
        foreach ((string rowGroup, string name, string value) in rows)
        {
            if (rowGroup != group)
            {
                group = rowGroup;
                builder.AppendLine($"[{group}]");
            }

            builder.AppendLine($"  {name}：{value}");
        }

        foreach (string note in notes)
        {
            builder.AppendLine($"  {note}");
        }

        builder.AppendLine();
        builder.AppendLine("说明：查看为只读；清理仅移除矩阵明确支持的元数据字段，不承诺正文/画面隐私清除。");

        File.WriteAllText(report, builder.ToString(), Encoding.UTF8);
        return report;
    }

    private static string Mode(JobRequest request) =>
        request.Parameters.TryGetValue("mode", out string? mode) && !string.IsNullOrWhiteSpace(mode)
            ? mode.Trim().ToLowerInvariant()
            : "view";

    private static bool Flag(IReadOnlyDictionary<string, string> parameters, string key) =>
        parameters.TryGetValue(key, out string? text) &&
        (text == "1" || (bool.TryParse(text, out bool parsed) && parsed));

    private static bool IsPdf(string path) =>
        PdfExtensions.Contains(Path.GetExtension(path));

    private static string HumanSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{value:0} {units[unit]}"
            : $"{value:0.#} {units[unit]}";
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long Number(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out JsonElement value))
        {
            return 0;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out long number) => number,
            JsonValueKind.String when long.TryParse(value.GetString(), out long parsed) => parsed,
            JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) => (long)d,
            _ => 0,
        };
    }

    private static JsonElement FirstOfType(JsonElement streams, string codecType)
    {
        foreach (JsonElement stream in streams.EnumerateArray())
        {
            if (Text(stream, "codec_type") == codecType)
            {
                return stream;
            }
        }

        return default;
    }

    private static string FormatDuration(string? seconds)
    {
        if (!double.TryParse(seconds, NumberStyles.Float, CultureInfo.InvariantCulture, out double total) ||
            total <= 0)
        {
            return "未知";
        }

        var span = TimeSpan.FromSeconds(total);
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:D2}:{span.Seconds:D2}"
            : $"{span.Minutes:D2}:{span.Seconds:D2}";
    }

    private static string FormatRate(string? rate)
    {
        if (string.IsNullOrEmpty(rate))
        {
            return "未知";
        }

        string[] parts = rate.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double numerator) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double denominator) &&
            denominator > 0)
        {
            return $"{numerator / denominator:0.###} fps";
        }

        return rate;
    }

    private static string Truncate(string text, int max = 2000)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max)
        {
            return text;
        }

        return text[..max] + " …(已截断)";
    }
}
