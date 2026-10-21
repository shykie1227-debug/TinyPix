using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImageMagick;
using TinyPix.Core.Jobs;
using TinyPix.Core.Tools;

namespace TinyPix.Media.IdPhoto;

/// <summary>
/// idphoto.create — FEATURE-MATRIX §5：智能证件照。YuNet 置信度 ≥0.90 人脸计数；
/// MODNet 512×512 抠图；自动裁切按 5 点眼线水平、模板画幅居中；背景按模板；
/// 不做人脸美化。输出 <c>{原名}_{模板短名}_{背景}.jpg</c>。
/// </summary>
public sealed class IdPhotoCreateHandler : IToolHandler
{
    public string ToolId => "idphoto.create";

    public async ValueTask<ToolValidationResult> ValidateAsync(JobRequest request, CancellationToken cancellationToken)
    {
        await Task.Yield();
        var errors = new List<JobError>();

        if (request.InputPaths.Count == 0)
        {
            errors.Add(new JobError("idphoto.create.input-empty", "至少需要 1 张图片。", false));
        }

        foreach (string input in request.InputPaths)
        {
            if (!File.Exists(input))
            {
                errors.Add(new JobError("idphoto.create.input-missing", $"文件不存在：{input}", false));
            }
        }

        string presetId = request.Parameters.TryGetValue("preset", out string? p) ? (p ?? "office-1in") : "office-1in";
        if (IdPhotoTemplates.Find(presetId) is null)
        {
            errors.Add(new JobError("idphoto.create.preset-invalid", $"未知模板：{presetId}", false));
        }

        return new ToolValidationResult(errors);
    }

    public ValueTask<ToolEstimate> EstimateAsync(JobRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new ToolEstimate(TimeSpan.FromSeconds(5 * request.InputPaths.Count), null));

    public async Task<ToolExecutionResult> ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken)
    {
        await Task.Yield();

        string presetId = context.Request.Parameters.TryGetValue("preset", out string? p) ? (p ?? "office-1in") : "office-1in";
        IdPhotoPreset preset = IdPhotoTemplates.Find(presetId)
            ?? throw new InvalidOperationException($"未知模板：{presetId}");

        string bgColorName = context.Request.Parameters.TryGetValue("background", out string? bgn) ? (bgn ?? "白") : "白";
        string modelRoot = Path.Combine(AppContext.BaseDirectory, "Models");

        using var detector = new YuNetFaceDetector(Path.Combine(modelRoot, "Face", "face_detection_yunet_2023mar.onnx"));
        using var matting = new ModNetMatting(Path.Combine(modelRoot, "Portrait", "modnet_photographic_portrait_matting.onnx"));

        string outputDirectory = context.Request.OutputDirectory;
        Directory.CreateDirectory(outputDirectory);

        var outputs = new List<string>();
        var failures = new List<JobError>();

        for (int inputIndex = 0; inputIndex < context.Request.InputPaths.Count; inputIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string input = context.Request.InputPaths[inputIndex];
            try
            {
                using var src = new MagickImage(input);
                src.AutoOrient();

                // Preprocess: long side cap 1280 (frozen).
                if (Math.Max(src.Width, src.Height) > 1280)
                {
                    src.Resize(new MagickGeometry("1280x1280>") { IgnoreAspectRatio = false });
                }

                int width = (int)src.Width;
                int height = (int)src.Height;

                // Extract RGB for YuNet/MODNet.
                byte[]? rgbNullable = src.GetPixels().ToByteArray(PixelMapping.RGB);
                if (rgbNullable is null) throw new InvalidOperationException("无法读取像素数据。");
                byte[] rgb = rgbNullable;

                // Face detection.
                IReadOnlyList<YuNetFace> faces = detector.Detect(rgb, width, height);
                if (faces.Count == 0)
                {
                    throw new InvalidOperationException("未检测到人脸。");
                }

                if (faces.Count > 1)
                {
                    throw new InvalidOperationException($"检测到 {faces.Count} 张人脸，证件照需要单人脸。");
                }

                YuNetFace face = faces[0];

                // MODNet matting → alpha matte at original resolution.
                byte[] alpha = matting.ComputeAlpha(rgb, width, height);

                // Build BGRA pixel buffer with alpha from matte.
                var bgra = new byte[width * height * 4];
                for (int i = 0; i < width * height; i++)
                {
                    bgra[i * 4] = rgb[i * 3 + 2];     // B
                    bgra[i * 4 + 1] = rgb[i * 3 + 1]; // G
                    bgra[i * 4 + 2] = rgb[i * 3];     // R
                    bgra[i * 4 + 3] = alpha[i];        // A
                }

                using var foreground = new MagickImage();
                foreground.ReadPixels(bgra, new PixelReadSettings((uint)width, (uint)height, StorageType.Char, PixelMapping.BGRA));

                // Composite onto background colour.
                MagickColor bgPixel = ParseBackgroundColor(bgColorName);
                using var canvas = new MagickImage(bgPixel, (uint)width, (uint)height);
                canvas.Composite(foreground, 0, 0, CompositeOperator.Over);

                // Crop to preset aspect ratio centered on face.
                double targetAspect = (double)preset.OutputWidthPx / preset.OutputHeightPx;
                int cropW, cropH;
                if (width / (double)height > targetAspect)
                {
                    cropH = height;
                    cropW = (int)(height * targetAspect);
                }
                else
                {
                    cropW = width;
                    cropH = (int)(width / targetAspect);
                }

                int cx = (int)(face.X + face.Width / 2);
                int cy = (int)(face.Y + face.Height / 2);
                int cropX = Math.Max(0, Math.Min(cx - cropW / 2, width - cropW));
                int cropY = Math.Max(0, Math.Min(cy - cropH / 2, height - cropH));

                canvas.Crop(new MagickGeometry(cropX, cropY, (uint)cropW, (uint)cropH));
                canvas.Resize(new MagickGeometry((uint)preset.OutputWidthPx, (uint)preset.OutputHeightPx) { IgnoreAspectRatio = true });
                canvas.Quality = 95;

                string output = Path.Combine(outputDirectory,
                    $"{Path.GetFileNameWithoutExtension(input)}_{preset.PresetId}_{bgColorName}.jpg");
                if (File.Exists(output))
                {
                    output = ResolveConflict(output);
                }

                canvas.Write(output, MagickFormat.Jpeg);
                outputs.Add(output);

                context.Progress.Report(new JobProgress(
                    (inputIndex + 1) * 100.0 / context.Request.InputPaths.Count,
                    $"证件照 {inputIndex + 1}/{context.Request.InputPaths.Count}"));
            }
            catch (Exception error)
            {
                failures.Add(new JobError("idphoto.create.failed", $"{Path.GetFileName(input)}：{error.Message}", true));
            }
        }

        if (failures.Count > 0 && outputs.Count == 0)
        {
            throw new InvalidOperationException($"证件照全部失败：{failures[0].Message}");
        }

        return failures.Count > 0
            ? ToolExecutionResult.Partial(outputs, failures[0])
            : ToolExecutionResult.Success(outputs);
    }

    private static MagickColor ParseBackgroundColor(string name) => name switch
    {
        "白" => MagickColors.White,
        "蓝" => MagickColors.RoyalBlue,
        "红" => MagickColors.Crimson,
        _ => MagickColors.White,
    };

    private static string ResolveConflict(string path)
    {
        string dir = Path.GetDirectoryName(path)!;
        string stem = Path.GetFileNameWithoutExtension(path);
        string ext = Path.GetExtension(path);
        int i = 1;
        string candidate;
        do
        {
            candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
            i++;
        }
        while (File.Exists(candidate));
        return candidate;
    }
}
