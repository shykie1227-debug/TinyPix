using System;
using System.Runtime.InteropServices;
using TinyPix.Capture.Interop;

namespace TinyPix.Capture;

/// <summary>
/// Minimal D3D11 surface/texture helpers for the capture pipeline.
/// Only three ID3D11DeviceContext methods are needed (Map/Unmap/CopyResource)
/// and one ID3D11Device method (CreateTexture2D + GetImmediateContext), so the
/// vtable slots are called through function pointers instead of declaring ~100
/// stub methods. Slot order is the frozen d3d11.h ABI (verified against
/// D3D11_SDK_VERSION headers); each constant documents its method.
/// </summary>
internal static unsafe class D3D11Helpers
{
    // ID3D11DeviceChild: GetDevice=3, SetPrivateData=4, SetPrivateDataInterface=5, GetPrivateData=6
    // ID3D11DeviceContext: VSSetConstantBuffers=7 ... Map=14, Unmap=15 ... CopySubresourceRegion=46, CopyResource=47
    private const int SlotMap = 14;
    private const int SlotUnmap = 15;
    private const int SlotCopyResource = 47;

    // ID3D11Device: CreateBuffer=3 ... CreateTexture2D=5 ... GetImmediateContext=40
    private const int DeviceSlotCreateTexture2D = 5;
    private const int DeviceSlotGetImmediateContext = 40;

    internal const int D3D11MapRead = 1;
    internal const int D3D11UsageStaging = 3;
    internal const int D3D11CpuAccessRead = 0x20000;
    internal const int DxgiFormatB8G8R8A8Unorm = 87;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Texture2DDesc
    {
        public uint Width;
        public uint Height;
        public uint MipLevels;
        public uint ArraySize;
        public int Format;
        public uint SampleCount;
        public uint SampleQuality;
        public int Usage;
        public uint BindFlags;
        public uint CpuAccessFlags;
        public uint MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MappedSubresource
    {
        public IntPtr Data;
        public uint RowPitch;
        public uint DepthPitch;
    }

    internal static IntPtr GetImmediateContext(IntPtr device)
    {
        var getContext = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, void>)(*(void***)device + DeviceSlotGetImmediateContext);
        IntPtr context;
        getContext(device, &context);
        return context;
    }

    internal static IntPtr CreateStagingTexture(IntPtr device, int width, int height)
    {
        var desc = new Texture2DDesc
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = DxgiFormatB8G8R8A8Unorm,
            SampleCount = 1,
            SampleQuality = 0,
            Usage = D3D11UsageStaging,
            BindFlags = 0,
            CpuAccessFlags = D3D11CpuAccessRead,
            MiscFlags = 0,
        };
        var create = (delegate* unmanaged[Stdcall]<IntPtr, Texture2DDesc*, IntPtr*, int>)(*(void***)device + DeviceSlotCreateTexture2D);
        IntPtr texture;
        int hr = create(device, &desc, &texture);
        HResult.ThrowIfFailed(hr, "ID3D11Device.CreateTexture2D(staging)");
        return texture;
    }

    internal static void CopyResource(IntPtr context, IntPtr destination, IntPtr source)
    {
        var copy = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, void>)(*(void***)context + SlotCopyResource);
        copy(context, destination, source);
    }

    internal static void MapRead(IntPtr context, IntPtr resource, out MappedSubresource mapped)
    {
        var map = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, int, uint, MappedSubresource*, int>)(*(void***)context + SlotMap);
        MappedSubresource local;
        int hr = map(context, resource, 0, D3D11MapRead, 0, &local);
        HResult.ThrowIfFailed(hr, "ID3D11DeviceContext.Map");
        mapped = local;
    }

    internal static void Unmap(IntPtr context, IntPtr resource)
    {
        var unmap = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, void>)(*(void***)context + SlotUnmap);
        unmap(context, resource, 0);
    }

    /// <summary>Read a GPU texture into a managed BGRA byte array (row-pitch aware).</summary>
    internal static byte[] ReadBgra(IntPtr context, IntPtr staging, int width, int height)
    {
        MapRead(context, staging, out MappedSubresource mapped);
        try
        {
            var pixels = new byte[width * height * 4];
            if (mapped.RowPitch == width * 4)
            {
                Marshal.Copy(mapped.Data, pixels, 0, pixels.Length);
            }
            else
            {
                for (int y = 0; y < height; y++)
                {
                    Marshal.Copy(
                        IntPtr.Add(mapped.Data, (int)(y * mapped.RowPitch)),
                        pixels,
                        y * width * 4,
                        width * 4);
                }
            }

            return pixels;
        }
        finally
        {
            Unmap(context, staging);
        }
    }

    /// <summary>QueryInterface helper over a raw COM pointer.</summary>
    internal static IntPtr QueryInterface(IntPtr comObject, ref Guid iid)
    {
        var query = (delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)(*(void***)comObject);
        Guid iidCopy = iid;
        IntPtr result;
        int hr = query(comObject, &iidCopy, &result);
        HResult.ThrowIfFailed(hr, "QueryInterface");
        return result;
    }

    internal static void Release(IntPtr comObject)
    {
        if (comObject != IntPtr.Zero)
        {
            Marshal.Release(comObject);
        }
    }
}
