using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace TinyPix.Media.IdPhoto;

/// <summary>
/// E5 MODNet 人像抠图（modnet_photographic_portrait_matting.onnx）：
/// 输入 512×512 RGB，输出 512×512 alpha matte。
/// 固定 512×512、batch=1、CPUExecutionProvider；单 session 复用；禁止并发（冻结规则）。
/// 模型路径：Models/Portrait/modnet_photographic_portrait_matting.onnx（钉版哈希见 DEPENDENCY-BASELINE §4.1）。
/// </summary>
public sealed class ModNetMatting : IDisposable
{
    private const int InputSize = 512;

    private readonly InferenceSession _session;
    private readonly object _lock = new();

    public ModNetMatting(string modelPath)
    {
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException($"MODNet 模型文件不存在：{modelPath}");
        }

        _session = new InferenceSession(modelPath);
    }

    /// <summary>
    /// Compute an alpha matte for the given RGB image. Returns a byte array of
    /// width×height alpha values (0=background, 255=foreground) at original resolution.
    /// </summary>
    public byte[] ComputeAlpha(byte[] rgb, int width, int height)
    {
        lock (_lock) // frozen: single session, no concurrent inference
        {
            // Resize to 512×512 (simple nearest-neighbor; output alpha is upsampled back).
            var inputData = new float[3 * InputSize * InputSize];
            float scaleX = (float)width / InputSize;
            float scaleY = (float)height / InputSize;

            for (int y = 0; y < InputSize; y++)
            {
                int srcY = Math.Min((int)(y * scaleY), height - 1);
                for (int x = 0; x < InputSize; x++)
                {
                    int srcX = Math.Min((int)(x * scaleX), width - 1);
                    int srcIdx = (srcY * width + srcX) * 3;
                    int dstIdx = y * InputSize + x;
                    inputData[dstIdx] = rgb[srcIdx] / 255.0f;
                    inputData[InputSize * InputSize + dstIdx] = rgb[srcIdx + 1] / 255.0f;
                    inputData[2 * InputSize * InputSize + dstIdx] = rgb[srcIdx + 2] / 255.0f;
                }
            }

            var inputTensor = new DenseTensor<float>(inputData, new[] { 1, 3, InputSize, InputSize });
            string inputName = _session.InputMetadata.Keys.First();
            var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(inputName, inputTensor) };

            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _session.Run(inputs);

            // Output: 1×1×512×512 alpha matte.
            float[] alphaData;
            foreach (DisposableNamedOnnxValue result in results)
            {
                if (result.Value is Tensor<float> tensor)
                {
                    alphaData = tensor.ToArray();
                    // Upsample 512×512 → original resolution (bilinear).
                    return UpsampleAlpha(alphaData, InputSize, InputSize, width, height);
                }
            }

            throw new InvalidOperationException("MODNet 未产生输出。");
        }
    }

    private static byte[] UpsampleAlpha(float[] alpha, int srcW, int srcH, int dstW, int dstH)
    {
        var output = new byte[dstW * dstH];
        float scaleX = (float)srcW / dstW;
        float scaleY = (float)srcH / dstH;

        for (int y = 0; y < dstH; y++)
        {
            float srcY = y * scaleY;
            int y0 = Math.Min((int)srcY, srcH - 1);
            int y1 = Math.Min(y0 + 1, srcH - 1);
            float fy = srcY - y0;

            for (int x = 0; x < dstW; x++)
            {
                float srcX = x * scaleX;
                int x0 = Math.Min((int)srcX, srcW - 1);
                int x1 = Math.Min(x0 + 1, srcW - 1);
                float fx = srcX - x0;

                float a00 = alpha[y0 * srcW + x0];
                float a01 = alpha[y0 * srcW + x1];
                float a10 = alpha[y1 * srcW + x0];
                float a11 = alpha[y1 * srcW + x1];

                float interpolated = a00 * (1 - fx) * (1 - fy) + a01 * fx * (1 - fy) +
                                     a10 * (1 - fx) * fy + a11 * fx * fy;
                output[y * dstW + x] = (byte)Math.Clamp((int)(interpolated * 255), 0, 255);
            }
        }

        return output;
    }

    public void Dispose() => _session.Dispose();
}
