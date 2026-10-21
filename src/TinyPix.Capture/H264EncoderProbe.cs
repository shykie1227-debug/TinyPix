using System;
using System.Runtime.InteropServices;
using TinyPix.Capture.Interop;

namespace TinyPix.Capture;

/// <summary>
/// E1 门禁（FEATURE-MATRIX §2.1）：任务入队前探测 Windows Media Foundation
/// H.264 编码器。缺失时冻结文案报错，禁止偷偷换用 GPL/软件编码器。
/// </summary>
public static class H264EncoderProbe
{
    private static readonly object Gate = new();
    private static bool _mfStarted;
    private static bool? _h264Available;

    public static bool IsAvailable()
    {
        lock (Gate)
        {
            if (_h264Available is not null)
            {
                return _h264Available.Value;
            }

            EnsureMediaFoundationStarted();
            _h264Available = EnumerateH264Encoders() > 0;
            return _h264Available.Value;
        }
    }

    public static void EnsureMediaFoundationStarted()
    {
        lock (Gate)
        {
            if (_mfStarted)
            {
                return;
            }

            HResult.ThrowIfFailed(
                NativeMethods.MFStartup(NativeMethods.MF_VERSION, NativeMethods.MFSTARTUP_FULL),
                "MFStartup");
            _mfStarted = true;
        }
    }

    /// <summary>Enumerate H.264 encoder MFTs (input NV12 → output H.264), returning the count.</summary>
    internal static int EnumerateH264Encoders()
    {
        IntPtr inputInfo = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.MftRegisterTypeInfo>());
        IntPtr outputInfo = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.MftRegisterTypeInfo>());
        try
        {
            var input = new NativeMethods.MftRegisterTypeInfo
            {
                GuidMajorType = NativeMethods.MFMediaType_Video,
                GuidSubtype = NativeMethods.MFVideoFormat_NV12,
            };
            var output = new NativeMethods.MftRegisterTypeInfo
            {
                GuidMajorType = NativeMethods.MFMediaType_Video,
                GuidSubtype = NativeMethods.MFVideoFormat_H264,
            };
            Marshal.StructureToPtr(input, inputInfo, false);
            Marshal.StructureToPtr(output, outputInfo, false);

            // MFT_ENUM_FLAG_ALL — presence probe, not a transcode-quality gate.
            int hr = NativeMethods.MFTEnumEx(
                NativeMethods.MFT_CATEGORY_VIDEO_ENCODER,
                0x3F,
                inputInfo,
                outputInfo,
                out IntPtr activateArray,
                out uint count);
            if (hr < 0)
            {
                return 0;
            }

            if (activateArray != IntPtr.Zero)
            {
                // The array holds IActivate* pointers we never use; free the container.
                for (uint i = 0; i < count; i++)
                {
                    IntPtr activation = Marshal.ReadIntPtr(activateArray, (int)(i * (uint)IntPtr.Size));
                    if (activation != IntPtr.Zero)
                    {
                        Marshal.Release(activation);
                    }
                }

                NativeMethods.CoTaskMemFree(activateArray);
            }

            return (int)count;
        }
        finally
        {
            Marshal.FreeHGlobal(inputInfo);
            Marshal.FreeHGlobal(outputInfo);
        }
    }
}
