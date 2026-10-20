using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace TinyPix.Media.IdPhoto;

/// <summary>
/// E4 YuNet 人脸检测（face_detection_yunet_2023mar.onnx）：
/// 输入 320×320 RGB blob，输出人脸框 + 5 点关键点（双眼/鼻/嘴角）。
/// 置信度 ≥ 0.90（FEATURE-MATRIX §5 冻结）。
/// 模型路径：Models/Face/face_detection_yunet_2023mar.onnx（钉版哈希见 DEPENDENCY-BASELINE §4）。
/// </summary>
public sealed class YuNetFaceDetector : IDisposable
{
    private const int InputSize = 320;
    private const float ConfidenceThreshold = 0.90f;

    private readonly InferenceSession _session;

    public YuNetFaceDetector(string modelPath)
    {
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException($"YuNet 模型文件不存在：{modelPath}");
        }

        _session = new InferenceSession(modelPath);
    }

    /// <summary>
    /// Detect faces in an RGB image (width × height, 3 bytes/pixel, row-major).
    /// Returns face boxes (x, y, w, h) with 5 landmarks each (10 floats: left_eye_x/y, right_eye_x/y, nose_x/y, mouth_left_x/y, mouth_right_x/y).
    /// </summary>
    public IReadOnlyList<YuNetFace> Detect(byte[] rgb, int width, int height)
    {
        // Resize to 320×320 with letterbox scaling.
        float scaleX = (float)InputSize / width;
        float scaleY = (float)InputSize / height;
        float scale = Math.Min(scaleX, scaleY);
        int scaledW = (int)(width * scale);
        int scaledH = (int)(height * scale);
        int padX = (InputSize - scaledW) / 2;
        int padY = (InputSize - scaledH) / 2;

        // Build input tensor: 1×3×320×320, normalized to [0,1] then standardized.
        var inputData = new float[3 * InputSize * InputSize];
        for (int y = 0; y < scaledH; y++)
        {
            int srcY = (int)(y / scale);
            for (int x = 0; x < scaledW; x++)
            {
                int srcX = (int)(x / scale);
                int srcIdx = (srcY * width + srcX) * 3;
                int dstIdx = ((y + padY) * InputSize + (x + padX));
                inputData[dstIdx] = (rgb[srcIdx] / 255.0f - 0.5f) / 0.5f;                  // R
                inputData[InputSize * InputSize + dstIdx] = (rgb[srcIdx + 1] / 255.0f - 0.5f) / 0.5f; // G
                inputData[2 * InputSize * InputSize + dstIdx] = (rgb[srcIdx + 2] / 255.0f - 0.5f) / 0.5f; // B
            }
        }

        var inputTensor = new DenseTensor<float>(inputData, new[] { 1, 3, InputSize, InputSize });
        string inputName = _session.InputMetadata.Keys.First();
        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(inputName, inputTensor) };

        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _session.Run(inputs);

        // Parse output: [num_detections, 15] = [x, y, w, h, score, 5×(x,y)]
        var faces = new List<YuNetFace>();
        foreach (DisposableNamedOnnxValue result in results)
        {
            if (result.Value is not Tensor<float> tensor || tensor.Dimensions.Length < 2)
            {
                continue;
            }

            int count = tensor.Dimensions[0];
            int dims = tensor.Dimensions[1];
            for (int i = 0; i < count; i++)
            {
                float score = tensor[i * dims + 4];
                if (score < ConfidenceThreshold)
                {
                    continue;
                }

                // Map from 320×320 space back to original image coordinates.
                float cx = tensor[i * dims + 0];
                float cy = tensor[i * dims + 1];
                float cw = tensor[i * dims + 2];
                float ch = tensor[i * dims + 3];

                float origX = (cx - padX) / scale;
                float origY = (cy - padY) / scale;
                float origW = cw / scale;
                float origH = ch / scale;

                var landmarks = new float[10];
                for (int j = 0; j < 10; j++)
                {
                    float val = tensor[i * dims + 5 + j];
                    landmarks[j] = (j % 2 == 0) ? (val - padX) / scale : (val - padY) / scale;
                }

                faces.Add(new YuNetFace(origX, origY, origW, origH, score, landmarks));
            }
        }

        return faces;
    }

    public void Dispose() => _session.Dispose();
}

/// <summary>Face detection result: bounding box (image coordinates) + 5 landmarks.</summary>
public sealed record YuNetFace(
    float X, float Y, float Width, float Height,
    float Confidence,
    float[] Landmarks) // [left_eye_x/y, right_eye_x/y, nose_x/y, mouth_l_x/y, mouth_r_x/y]
{
    public float LeftEyeX => Landmarks[0];
    public float LeftEyeY => Landmarks[1];
    public float RightEyeX => Landmarks[2];
    public float RightEyeY => Landmarks[3];
    public float NoseX => Landmarks[4];
    public float NoseY => Landmarks[5];
    public float EyeLineY => (LeftEyeY + RightEyeY) / 2;
}
