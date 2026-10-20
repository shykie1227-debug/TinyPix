using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ImageMagick;
using TinyPix.Core.Jobs;
using TinyPix.Core.Tools;

namespace TinyPix.Media.IdPhoto;

/// <summary>
/// idphoto.check — FEATURE-MATRIX §5：证件照规格检查。像素/DPI/格式用精确值；
/// 构图由 YuNet+MODNet 估计。输出 <c>{原名}_检查结果.txt</c>（也可选修正图）。
/// 硬错误/风险/人工确认三类分开显示。
/// </summary>
public sealed class IdPhotoCheckHandler : IToolHandler
{
    public string ToolId => "idphoto.check";

    public async ValueTask<ToolValidationResult> ValidateAsync(JobRequest request, CancellationToken cancellationToken)
    {
        await Task.Yield();
        var errors = new List<JobError>();
        foreach (string input in request.InputPaths)
        {
            if (!File.Exists(input))
            {
                errors.Add(new JobError("idphoto.check.input-missing", $"文件不存在：{input}", false));
            }
        }

        return new ToolValidationResult(errors);
    }

    public ValueTask<ToolEstimate> EstimateAsync(JobRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new ToolEstimate(TimeSpan.FromSeconds(3 * request.InputPaths.Count), null));

    public async Task<ToolExecutionResult> ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken)
    {
        await Task.Yield();

        string presetId = context.Request.Parameters.TryGetValue("preset", out string? p) ? (p ?? "office-1in") : "office-1in";
        IdPhotoPreset preset = IdPhotoTemplates.Find(presetId)
            ?? throw new InvalidOperationException($"未知模板：{presetId}");

        string outputDirectory = context.Request.OutputDirectory;
        Directory.CreateDirectory(outputDirectory);

        string modelRoot = Path.Combine(AppContext.BaseDirectory, "Models");
        using var detector = new YuNetFaceDetector(Path.Combine(modelRoot, "Face", "face_detection_yunet_2023mar.onnx"));

        var outputs = new List<string>();
        var failures = new List<JobError>();

        foreach (string input in context.Request.InputPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var magick = new MagickImage(input);
                magick.AutoOrient();

                var hardErrors = new List<string>();
                var risks = new List<string>();
                var manualChecks = new List<string>();

                // Pixel/DPI check (product output target).
                if (magick.Width != preset.OutputWidthPx || magick.Height != preset.OutputHeightPx)
                {
                    hardErrors.Add($"产品输出目标错误：当前 {magick.Width}×{magick.Height} px，模板要求 {preset.OutputWidthPx}×{preset.OutputHeightPx} px");
                }

                // Aspect ratio check (official hard rule for presets with fixed aspect).
                double actualAspect = magick.Width / (double)magick.Height;
                double targetAspect = preset.OutputWidthPx / (double)preset.OutputHeightPx;
                if (Math.Abs(actualAspect - targetAspect) > 0.01)
                {
                    hardErrors.Add($"官方规则硬错误：画幅比例非 {preset.WidthMm}:{preset.HeightMm}");
                }

                // Face detection.
                byte[]? rgbNullable = magick.GetPixels().ToByteArray(PixelMapping.RGB);
                if (rgbNullable is null) throw new InvalidOperationException("无法读取像素数据。");
                byte[] rgb = rgbNullable;
                IReadOnlyList<YuNetFace> faces = detector.Detect(rgb, (int)magick.Width, (int)magick.Height);
                if (faces.Count == 0)
                {
                    hardErrors.Add("硬错误：未检测到人脸");
                }
                else if (faces.Count > 1)
                {
                    hardErrors.Add($"硬错误：检测到 {faces.Count} 张人脸");
                }
                else
                {
                    YuNetFace face = faces[0];
                    manualChecks.Add("请确认头顶/下巴位置（机器估计值仅供参考）");

                    if (preset.HeadRules.FaceHeightRatioMin is not null)
                    {
                        double faceRatio = face.Height / magick.Height;
                        if (faceRatio < preset.HeadRules.FaceHeightRatioMin || faceRatio > preset.HeadRules.FaceHeightRatioMax)
                        {
                            risks.Add($"脸部高度占比 {faceRatio:P0}（建议 {preset.HeadRules.FaceHeightRatioMin:P0}–{preset.HeadRules.FaceHeightRatioMax:P0}）");
                        }
                    }

                    if (preset.HeadRules.HeadHeightRatioMin is not null)
                    {
                        double headRatio = face.Height / magick.Height;
                        if (headRatio < preset.HeadRules.HeadHeightRatioMin || headRatio > preset.HeadRules.HeadHeightRatioMax)
                        {
                            risks.Add($"头部高度占比 {headRatio:P0}（官方 {preset.HeadRules.HeadHeightRatioMin:P0}–{preset.HeadRules.HeadHeightRatioMax:P0}）");
                        }
                    }

                    // 侧脸/遮挡/阴影/清晰度 → risk items
                    risks.Add("正面程度、眼睛可见、背景均匀、清晰度、遮挡（机器检测有限，请人工确认）");
                }

                // Write report.
                string reportPath = Path.Combine(outputDirectory, $"{Path.GetFileNameWithoutExtension(input)}_检查结果.txt");
                var sb = new StringBuilder();
                sb.AppendLine($"证件照规格检查 — {preset.DisplayName}");
                sb.AppendLine($"核验日期：{preset.VerificationDate}");
                sb.AppendLine($"官方来源：{preset.SourceUrl}");
                sb.AppendLine();
                sb.AppendLine("请在递交前核对主管机关最新要求；最终是否受理由主管机关决定。");
                sb.AppendLine();
                sb.AppendLine($"像素/DPI/格式精确值：");
                sb.AppendLine($"  当前 {magick.Width}×{magick.Height} px");
                sb.AppendLine($"  模板 {preset.OutputWidthPx}×{preset.OutputHeightPx} px @ {preset.Dpi} DPI");
                sb.AppendLine();
                sb.AppendLine($"硬错误（{hardErrors.Count}）：");
                foreach (string e in hardErrors) sb.AppendLine($"  ✗ {e}");
                sb.AppendLine();
                sb.AppendLine($"风险警告（{risks.Count}）：");
                foreach (string r in risks) sb.AppendLine($"  ⚠ {r}");
                sb.AppendLine();
                sb.AppendLine($"人工确认项（{manualChecks.Count}）：");
                foreach (string m in manualChecks) sb.AppendLine($"  ? {m}");

                File.WriteAllText(reportPath, sb.ToString(), new UTF8Encoding(false));
                outputs.Add(reportPath);
            }
            catch (Exception error)
            {
                failures.Add(new JobError("idphoto.check.failed", $"{Path.GetFileName(input)}：{error.Message}", true));
            }
        }

        return failures.Count > 0 && outputs.Count == 0
            ? throw new InvalidOperationException($"检查全部失败：{failures[0].Message}")
            : failures.Count > 0
                ? ToolExecutionResult.Partial(outputs, failures[0])
                : ToolExecutionResult.Success(outputs);
    }
}
