using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using TinyPix.Capture.Interop;
using TinyPix.Media.Capture;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace TinyPix.Capture;

/// <summary>
/// capture.record 的唯一引擎实现（FEATURE-MATRIX §8）：
/// E11 Windows Graphics Capture 取帧 → E1 Media Foundation H.264/AAC 封装 MP4。
/// 超大区域等比缩至 1080p 且不放大；音频（系统音频回环/麦克风）默认关闭；
/// 含光标可选；失败不留下损坏输出文件。
/// </summary>
public sealed class WindowsScreenCaptureEngine : IScreenCaptureEngine
{
    /// <summary>IGraphicsCaptureItem IID — discovered from the projection, with the
    /// documented fallback (IGraphicsCaptureItemInterop sample).</summary>
    private static readonly Guid GraphicsCaptureItemIid = DiscoverItemIid();

    public Task<CapturePreflightResult> PreflightAsync(CancellationToken cancellationToken)
    {
        bool available = H264EncoderProbe.IsAvailable();
        return Task.FromResult(available
            ? CapturePreflightResult.Available()
            : new CapturePreflightResult(false, "此电脑缺少可用的 Windows H.264 编码器"));
    }

    public Task<IScreenCaptureSession> StartAsync(
        ScreenCaptureTarget target,
        ScreenCaptureProfile profile,
        string outputPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (!H264EncoderProbe.IsAvailable())
        {
            throw new InvalidOperationException("此电脑缺少可用的 Windows H.264 编码器");
        }

        H264EncoderProbe.EnsureMediaFoundationStarted();
        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        (GraphicsCaptureItem item, CaptureGeometry geometry) = CreateCaptureItem(target);

        var session = new WindowsCaptureSession(item, geometry, profile, outputPath);
        session.Start();
        return Task.FromResult<IScreenCaptureSession>(session);
    }

    /// <summary>Frame-space geometry: WGC frame size + optional crop + output size.</summary>
    internal sealed record CaptureGeometry(
        int FrameWidth,
        int FrameHeight,
        int CropX,
        int CropY,
        int CropWidth,
        int CropHeight,
        int OutputWidth,
        int OutputHeight,
        string OwnerDescription);

    private static (GraphicsCaptureItem, CaptureGeometry) CreateCaptureItem(ScreenCaptureTarget target)
    {
        HResult.ThrowIfFailed(
            NativeMethods.WindowsCreateString("Windows.Graphics.Capture.GraphicsCaptureItem", 0, out IntPtr className),
            "WindowsCreateString");
        try
        {
            Guid iidInterop = typeof(NativeMethods.IGraphicsCaptureItemInterop).GUID;
            HResult.ThrowIfFailed(
                NativeMethods.RoGetActivationFactory(className, ref iidInterop, out IntPtr factory),
                "RoGetActivationFactory(GraphicsCaptureItem)");
            var interop = (NativeMethods.IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factory);
            Marshal.Release(factory);

            Guid iidItem = GraphicsCaptureItemIid;
            IntPtr itemPointer;
            int cropX = 0, cropY = 0;
            string owner;

            switch (target)
            {
                case WindowCaptureTarget window:
                    HResult.ThrowIfFailed(
                        interop.CreateForWindow(new IntPtr(window.WindowHandle), ref iidItem, out itemPointer),
                        "CreateForWindow");
                    owner = "窗口";
                    break;

                case MonitorCaptureTarget monitor:
                {
                    IntPtr hMonitor = ResolveMonitorHandle(monitor.MonitorIndex);
                    HResult.ThrowIfFailed(
                        interop.CreateForMonitor(hMonitor, ref iidItem, out itemPointer),
                        "CreateForMonitor");
                    owner = $"屏幕 {monitor.MonitorIndex}";
                    break;
                }

                case RegionCaptureTarget region:
                {
                    var point = new NativeMethods.POINT
                    {
                        X = region.X + region.Width / 2,
                        Y = region.Y + region.Height / 2,
                    };
                    IntPtr hMonitor = NativeMethods.MonitorFromPoint(point, 2 /* MONITOR_DEFAULTTONEAREST */);
                    if (hMonitor == IntPtr.Zero)
                    {
                        throw new InvalidOperationException("框选区域不在任何显示器范围内。");
                    }

                    HResult.ThrowIfFailed(
                        interop.CreateForMonitor(hMonitor, ref iidItem, out itemPointer),
                        "CreateForMonitor(region)");

                    var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
                    if (!NativeMethods.GetMonitorInfo(hMonitor, ref info))
                    {
                        throw new InvalidOperationException("GetMonitorInfo 失败，无法映射框选区域。");
                    }

                    // Frozen mapping rule: logical coordinates → physical pixels.
                    NativeMethods.GetDpiForMonitor(hMonitor, 0, out uint dpiX, out _);
                    double scale = dpiX / 96.0;
                    cropX = (int)Math.Round((region.X - info.RcMonitor.Left) * scale);
                    cropY = (int)Math.Round((region.Y - info.RcMonitor.Top) * scale);
                    int cropWidth = (int)Math.Round(region.Width * scale);
                    int cropHeight = (int)Math.Round(region.Height * scale);
                    owner = $"区域 {region.Width}×{region.Height}";

                    GraphicsCaptureItem regionItem = GraphicsCaptureItem.FromAbi(itemPointer);
                    int frameWidth = regionItem.Size.Width;
                    int frameHeight = regionItem.Size.Height;
                    if (cropX < 0 || cropY < 0 || cropWidth <= 0 || cropHeight <= 0 ||
                        cropX + cropWidth > frameWidth || cropY + cropHeight > frameHeight)
                    {
                        throw new InvalidOperationException(
                            $"框选区域超出屏幕范围：{region.X},{region.Y},{region.Width}×{region.Height}");
                    }

                    return (regionItem, BuildGeometry(
                        frameWidth, frameHeight, cropX, cropY, cropWidth, cropHeight, owner));
                }

                default:
                    throw new InvalidOperationException($"未知录制目标：{target.GetType().Name}");
            }

            GraphicsCaptureItem item = GraphicsCaptureItem.FromAbi(itemPointer);
            return (item, BuildGeometry(
                item.Size.Width, item.Size.Height, 0, 0, item.Size.Width, item.Size.Height, owner));
        }
        finally
        {
            NativeMethods.WindowsDeleteString(className);
        }
    }

    private static CaptureGeometry BuildGeometry(
        int frameWidth, int frameHeight, int cropX, int cropY, int cropWidth, int cropHeight, string owner)
    {
        // Output obeys the frozen standard-quality cap: scale down to 1080p, never up, keep even.
        const int capWidth = 1920;
        const int capHeight = 1080;
        double scale = Math.Min(1.0, Math.Min(capWidth / (double)cropWidth, capHeight / (double)cropHeight));
        int outputWidth = Math.Max(2, (int)(cropWidth * scale) & ~1);
        int outputHeight = Math.Max(2, (int)(cropHeight * scale) & ~1);
        return new CaptureGeometry(
            frameWidth, frameHeight, cropX, cropY, cropWidth, cropHeight,
            outputWidth, outputHeight, owner);
    }

    private static Guid DiscoverItemIid()
    {
        try
        {
            Type? abiType = typeof(GraphicsCaptureItem).Assembly
                .GetType("ABI.Windows.Graphics.Capture.IGraphicsCaptureItem");
            if (abiType is not null && abiType.GUID != Guid.Empty)
            {
                return abiType.GUID;
            }
        }
        catch
        {
            // fall through to the documented constant
        }

        return new Guid("79c3f95b-31f7-4e11-a9a5-0c66d58a1b8f");
    }

    private static IntPtr ResolveMonitorHandle(int monitorIndex)
    {
        var handles = new System.Collections.Generic.List<IntPtr>();
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr hMonitor, IntPtr hdc, ref NativeMethods.RECT rect, IntPtr data) =>
            {
                handles.Add(hMonitor);
                return true;
            },
            IntPtr.Zero);
        if (handles.Count == 0)
        {
            throw new InvalidOperationException("未检测到任何显示器。");
        }

        if (monitorIndex >= handles.Count)
        {
            throw new InvalidOperationException($"显示器序号 {monitorIndex} 超出范围（共 {handles.Count} 台）。");
        }

        return handles[monitorIndex];
    }
}

/// <summary>
/// A running capture: WGC frames → CPU crop/downscale/NV12 → MF H.264 samples,
/// plus optional WASAPI audio → AAC samples in the same MP4. Stop finalizes the
/// output ("停止后进入普通输出状态"); failures delete the incomplete file.
/// </summary>
internal sealed class WindowsCaptureSession : IScreenCaptureSession
{
    private readonly GraphicsCaptureItem _item;
    private readonly WindowsScreenCaptureEngine.CaptureGeometry _geometry;
    private readonly ScreenCaptureProfile _profile;
    private readonly string _outputPath;
    private readonly TaskCompletionSource<CaptureSessionResult> _stopped =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _writeGate = new();

    private IntPtr _d3dDevice;
    private IntPtr _d3dContext;
    private IntPtr _staging;
    private IDirect3DDevice? _winrtDevice;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _captureSession;
    private NativeMethods.IMFSinkWriter? _sinkWriter;
    private uint _videoStreamIndex;
    private uint _audioStreamIndex = uint.MaxValue;
    private WasapiCaptureSource? _loopback;
    private WasapiCaptureSource? _microphone;
    private PcmAudioMixer? _mixer;
    private Timer? _audioPump;

    private long _firstFrameTicks = -1;
    private long _lastFrameTicks;
    private long _framesWritten;
    private Exception? _failure;
    private bool _finalized;

    public WindowsCaptureSession(
        GraphicsCaptureItem item,
        WindowsScreenCaptureEngine.CaptureGeometry geometry,
        ScreenCaptureProfile profile,
        string outputPath)
    {
        _item = item;
        _geometry = geometry;
        _profile = profile;
        _outputPath = outputPath;
    }

    public string OutputPath => _outputPath;

    public TimeSpan Elapsed
    {
        get
        {
            long first = Interlocked.Read(ref _firstFrameTicks);
            long last = Interlocked.Read(ref _lastFrameTicks);
            return first < 0 || last < first ? TimeSpan.Zero : TimeSpan.FromTicks(last - first);
        }
    }

    public void Start()
    {
        try
        {
            StartCore();
        }
        catch (Exception exception)
        {
            _failure = exception;
            _stopped.TrySetResult(FailedResult());
            throw;
        }
    }

    private void StartCore()
    {
        // 1) D3D11 device + WinRT interop device for the frame pool.
        HResult.ThrowIfFailed(
            NativeMethods.D3D11CreateDevice(
                IntPtr.Zero,
                NativeMethods.D3D_DRIVER_TYPE_HARDWARE,
                IntPtr.Zero,
                NativeMethods.D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                IntPtr.Zero,
                0,
                7, // D3D11_SDK_VERSION
                out _d3dDevice,
                out _,
                out _d3dContext),
            "D3D11CreateDevice");

        Guid iidDxgiDevice = typeof(NativeMethods.IDXGIDevice).GUID;
        IntPtr dxgiDevice = D3D11Helpers.QueryInterface(_d3dDevice, ref iidDxgiDevice);
        try
        {
            HResult.ThrowIfFailed(
                NativeMethods.CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out IntPtr winrtDevicePointer),
                "CreateDirect3D11DeviceFromDXGIDevice");
            _winrtDevice = MarshalInterface<IDirect3DDevice>.FromAbi(winrtDevicePointer);
            Marshal.Release(winrtDevicePointer);
        }
        finally
        {
            D3D11Helpers.Release(dxgiDevice);
        }

        // Staging textures: one for GPU→CPU readback, one spare frame surface target.
        _staging = D3D11Helpers.CreateStagingTexture(_d3dDevice, _geometry.FrameWidth, _geometry.FrameHeight);

        // 2) Media Foundation sink writer: H.264 video (+ optional AAC audio).
        HResult.ThrowIfFailed(
            NativeMethods.MFCreateSinkWriterFromURL(_outputPath, IntPtr.Zero, IntPtr.Zero, out _sinkWriter),
            "MFCreateSinkWriterFromURL");
        ConfigureVideoStream();
        if (_profile.SystemAudio || _profile.Microphone)
        {
            ConfigureAudioStream();
        }

        HResult.ThrowIfFailed(_sinkWriter!.BeginWriting(), "IMFSinkWriter.BeginWriting");

        // 3) WGC item + free-threaded frame pool sized to the item.
        _item.Closed += OnTargetClosed;
        _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _winrtDevice!,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            2,
            new Windows.Graphics.SizeInt32(_geometry.FrameWidth, _geometry.FrameHeight));
        _framePool.FrameArrived += OnFrameArrived;
        _captureSession = _framePool.CreateCaptureSession(_item);
        _captureSession.IsCursorCaptureEnabled = _profile.IncludeCursor;
        _captureSession.StartCapture();

        // 4) Audio capture sources + pump (defaults are off).
        if (_profile.SystemAudio || _profile.Microphone)
        {
            _mixer = new PcmAudioMixer(_profile.AudioSampleRate, 2);
            if (_profile.SystemAudio)
            {
                _loopback = WasapiCaptureSource.Start(loopback: true);
                _loopback.PacketCaptured += _mixer.Submit;
            }

            if (_profile.Microphone)
            {
                _microphone = WasapiCaptureSource.Start(loopback: false);
                _microphone.PacketCaptured += _mixer.Submit;
            }

            _audioPump = new Timer(_ => PumpAudio(flush: false), null, 100, 100);
        }
    }

    private void ConfigureVideoStream()
    {
        NativeMethods.MFCreateMediaType(out NativeMethods.IMFMediaType outputType)
            .Throw("MFCreateMediaType(video out)");
        SetGuid(outputType, NativeMethods.MF_MT_MAJOR_TYPE, NativeMethods.MFMediaType_Video);
        SetGuid(outputType, NativeMethods.MF_MT_SUBTYPE, NativeMethods.MFVideoFormat_H264);
        SetUint(outputType, NativeMethods.MF_MT_AVG_BITRATE, (uint)_profile.VideoBitrate);
        SetUint64(outputType, NativeMethods.MF_MT_FRAME_SIZE, PackSize(_geometry.OutputWidth, _geometry.OutputHeight));
        SetUint64(outputType, NativeMethods.MF_MT_FRAME_RATE, PackSize(_profile.FrameRate, 1));
        SetUint(outputType, NativeMethods.MF_MT_INTERLACE_MODE, NativeMethods.MFVideoInterlace_Progressive);
        SetUint64(outputType, NativeMethods.MF_MT_PIXEL_ASPECT_RATIO, PackSize(1, 1));
        _sinkWriter!.AddStream(outputType, out _videoStreamIndex).Throw("AddStream(video)");

        NativeMethods.MFCreateMediaType(out NativeMethods.IMFMediaType inputType)
            .Throw("MFCreateMediaType(video in)");
        SetGuid(inputType, NativeMethods.MF_MT_MAJOR_TYPE, NativeMethods.MFMediaType_Video);
        SetGuid(inputType, NativeMethods.MF_MT_SUBTYPE, NativeMethods.MFVideoFormat_NV12);
        SetUint64(inputType, NativeMethods.MF_MT_FRAME_SIZE, PackSize(_geometry.OutputWidth, _geometry.OutputHeight));
        SetUint64(inputType, NativeMethods.MF_MT_FRAME_RATE, PackSize(_profile.FrameRate, 1));
        SetUint(inputType, NativeMethods.MF_MT_INTERLACE_MODE, NativeMethods.MFVideoInterlace_Progressive);
        SetUint64(inputType, NativeMethods.MF_MT_PIXEL_ASPECT_RATIO, PackSize(1, 1));
        _sinkWriter.SetInputMediaType(_videoStreamIndex, inputType, IntPtr.Zero)
            .Throw("SetInputMediaType(video)");
    }

    private void ConfigureAudioStream()
    {
        NativeMethods.MFCreateMediaType(out NativeMethods.IMFMediaType outputType)
            .Throw("MFCreateMediaType(audio out)");
        SetGuid(outputType, NativeMethods.MF_MT_MAJOR_TYPE, NativeMethods.MFMediaType_Audio);
        SetGuid(outputType, NativeMethods.MF_MT_SUBTYPE, NativeMethods.MFAudioFormat_AAC);
        SetUint(outputType, NativeMethods.MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
        SetUint(outputType, NativeMethods.MF_MT_AUDIO_SAMPLES_PER_SECOND, (uint)_profile.AudioSampleRate);
        SetUint(outputType, NativeMethods.MF_MT_AUDIO_NUM_CHANNELS, 2);
        SetUint(outputType, NativeMethods.MF_MT_AUDIO_AVG_BYTES_PER_SECOND, (uint)(_profile.AudioBitrate / 8));
        SetUint(outputType, NativeMethods.MF_MT_AAC_PAYLOAD_TYPE, 0);
        _sinkWriter!.AddStream(outputType, out _audioStreamIndex).Throw("AddStream(audio)");

        NativeMethods.MFCreateMediaType(out NativeMethods.IMFMediaType inputType)
            .Throw("MFCreateMediaType(audio in)");
        SetGuid(inputType, NativeMethods.MF_MT_MAJOR_TYPE, NativeMethods.MFMediaType_Audio);
        SetGuid(inputType, NativeMethods.MF_MT_SUBTYPE, NativeMethods.MFAudioFormat_PCM);
        SetUint(inputType, NativeMethods.MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
        SetUint(inputType, NativeMethods.MF_MT_AUDIO_SAMPLES_PER_SECOND, (uint)_profile.AudioSampleRate);
        SetUint(inputType, NativeMethods.MF_MT_AUDIO_NUM_CHANNELS, 2);
        SetUint(inputType, NativeMethods.MF_MT_AUDIO_BLOCK_ALIGNMENT, 4);
        SetUint(inputType, NativeMethods.MF_MT_AUDIO_AVG_BYTES_PER_SECOND, (uint)(_profile.AudioSampleRate * 4));
        _sinkWriter.SetInputMediaType(_audioStreamIndex, inputType, IntPtr.Zero)
            .Throw("SetInputMediaType(audio)");
    }

    private void OnTargetClosed(GraphicsCaptureItem sender, object args) =>
        _failure ??= new InvalidOperationException("捕获目标已关闭（窗口关闭或设备丢失）。");

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        if (_failure is not null || _sinkWriter is null)
        {
            return;
        }

        try
        {
            Direct3D11CaptureFrame? frame = sender.TryGetNextFrame();
            if (frame is null)
            {
                return;
            }

            using (frame)
            {
                long frameTicks = frame.SystemRelativeTime.Ticks;
                long first = Interlocked.Read(ref _firstFrameTicks);
                if (first < 0)
                {
                    Interlocked.CompareExchange(ref _firstFrameTicks, frameTicks, -1);
                    first = frameTicks;
                }

                Interlocked.Exchange(ref _lastFrameTicks, frameTicks);

                IntPtr texture = GetTexturePointer(frame.Surface);
                try
                {
                    D3D11Helpers.CopyResource(_d3dContext, _staging, texture);
                }
                finally
                {
                    D3D11Helpers.Release(texture);
                }

                byte[] bgra = D3D11Helpers.ReadBgra(
                    _d3dContext, _staging, _geometry.FrameWidth, _geometry.FrameHeight);
                long durationTicks = TimeSpan.TicksPerSecond / _profile.FrameRate;

                lock (_writeGate)
                {
                    WriteVideoFrame(bgra, frameTicks - first, durationTicks);
                    _framesWritten++;
                }
            }
        }
        catch (Exception exception)
        {
            _failure = exception;
        }
    }

    private void WriteVideoFrame(byte[] bgra, long sampleTicks, long durationTicks)
    {
        int nv12Length = _geometry.OutputWidth * _geometry.OutputHeight * 3 / 2;
        NativeMethods.MFCreateMemoryBuffer((uint)nv12Length, out NativeMethods.IMFMediaBuffer buffer)
            .Throw("MFCreateMemoryBuffer");
        try
        {
            buffer.Lock(out IntPtr data, out _, out _).Throw("IMFMediaBuffer.Lock");
            try
            {
                var nv12 = new byte[nv12Length];
                Nv12FrameConverter.ConvertBgraToNv12(
                    bgra,
                    _geometry.FrameWidth,
                    _geometry.FrameHeight,
                    _geometry.CropX,
                    _geometry.CropY,
                    _geometry.CropWidth,
                    _geometry.CropHeight,
                    nv12,
                    _geometry.OutputWidth,
                    _geometry.OutputHeight);
                Marshal.Copy(nv12, 0, data, nv12Length);
            }
            finally
            {
                buffer.Unlock().Throw("IMFMediaBuffer.Unlock");
            }

            buffer.SetCurrentLength((uint)nv12Length).Throw("SetCurrentLength");
            NativeMethods.MFCreateSample(out NativeMethods.IMFSample sample).Throw("MFCreateSample");
            try
            {
                sample.AddBuffer(buffer).Throw("AddBuffer");
                sample.SetSampleTime(sampleTicks).Throw("SetSampleTime");
                sample.SetSampleDuration(durationTicks).Throw("SetSampleDuration");
                _sinkWriter!.WriteSample(_videoStreamIndex, sample).Throw("WriteSample(video)");
            }
            finally
            {
                Marshal.ReleaseComObject(sample);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(buffer);
        }
    }

    private void PumpAudio(bool flush)
    {
        if (_mixer is null || _sinkWriter is null || _audioStreamIndex == uint.MaxValue)
        {
            return;
        }

        try
        {
            byte[] pcm = _mixer.PullPcm16(_profile.AudioSampleRate / 2, out int frames, flush);
            if (frames <= 0)
            {
                return;
            }

            long startFrame = _mixer.NextFrame - frames;
            long sampleTime = startFrame * 10_000_000L / _profile.AudioSampleRate;
            long duration = frames * 10_000_000L / _profile.AudioSampleRate;

            NativeMethods.MFCreateMemoryBuffer((uint)pcm.Length, out NativeMethods.IMFMediaBuffer buffer)
                .Throw("MFCreateMemoryBuffer(audio)");
            try
            {
                buffer.Lock(out IntPtr data, out _, out _).Throw("Lock(audio)");
                Marshal.Copy(pcm, 0, data, pcm.Length);
                buffer.Unlock().Throw("Unlock(audio)");
                buffer.SetCurrentLength((uint)pcm.Length).Throw("SetCurrentLength(audio)");
                NativeMethods.MFCreateSample(out NativeMethods.IMFSample sample).Throw("MFCreateSample(audio)");
                try
                {
                    sample.AddBuffer(buffer).Throw("AddBuffer(audio)");
                    sample.SetSampleTime(sampleTime).Throw("SetSampleTime(audio)");
                    sample.SetSampleDuration(duration).Throw("SetSampleDuration(audio)");
                    lock (_writeGate)
                    {
                        _sinkWriter.WriteSample(_audioStreamIndex, sample).Throw("WriteSample(audio)");
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(sample);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(buffer);
            }
        }
        catch (Exception exception)
        {
            _failure ??= exception;
        }
    }

    public Task<CaptureSessionResult> StopAsync(CancellationToken cancellationToken)
    {
        if (_finalized)
        {
            return _stopped.Task;
        }

        _finalized = true;
        try
        {
            // Stop accepting frames first, then flush encoders.
            try { _captureSession?.Dispose(); } catch { }
            try { _framePool!.FrameArrived -= OnFrameArrived; } catch { }
            try { _framePool?.Dispose(); } catch { }
            _audioPump?.Dispose();
            PumpAudio(flush: true);
            _loopback?.Dispose();
            _microphone?.Dispose();

            lock (_writeGate)
            {
                if (_failure is null)
                {
                    _sinkWriter!.EndWriting().Throw("IMFSinkWriter.EndWriting");
                }
            }

            if (_failure is not null)
            {
                TryDeleteIncompleteOutput();
                _stopped.TrySetResult(FailedResult());
                return _stopped.Task;
            }

            TimeSpan duration = Elapsed;
            if (duration <= TimeSpan.Zero && _framesWritten > 0)
            {
                duration = TimeSpan.FromTicks(_framesWritten * TimeSpan.TicksPerSecond / _profile.FrameRate);
            }

            _stopped.TrySetResult(new CaptureSessionResult(
                _outputPath,
                duration,
                _geometry.OutputWidth,
                _geometry.OutputHeight,
                _profile.FrameRate,
                CaptureOutputNaming.AudioSourceLabel(_profile.SystemAudio, _profile.Microphone)));
        }
        catch (Exception exception)
        {
            _failure = exception;
            TryDeleteIncompleteOutput();
            _stopped.TrySetResult(FailedResult());
        }

        return _stopped.Task;
    }

    private CaptureSessionResult FailedResult() => new(
        _outputPath, Elapsed, _geometry.OutputWidth, _geometry.OutputHeight, _profile.FrameRate,
        CaptureOutputNaming.AudioSourceLabel(_profile.SystemAudio, _profile.Microphone));

    private void TryDeleteIncompleteOutput()
    {
        try
        {
            if (File.Exists(_outputPath))
            {
                File.Delete(_outputPath);
            }
        }
        catch
        {
            // best effort; the frozen rule only requires no source-file damage
        }
    }

    private static IntPtr GetTexturePointer(IDirect3DSurface surface)
    {
        Guid iidTexture = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c"); // ID3D11Texture2D
        NativeMethods.IDirect3DDxgiInterfaceAccess access =
            surface.As<NativeMethods.IDirect3DDxgiInterfaceAccess>();
        access.GetInterface(ref iidTexture, out IntPtr texture)
            .Throw("GetInterface(ID3D11Texture2D)");
        return texture;
    }

    private static void SetGuid(NativeMethods.IMFMediaType type, Guid key, Guid value) =>
        type.SetGUID(ref key, ref value).Throw("SetGUID");

    private static void SetUint(NativeMethods.IMFMediaType type, Guid key, uint value) =>
        type.SetUINT32(ref key, value).Throw("SetUINT32");

    private static void SetUint64(NativeMethods.IMFMediaType type, Guid key, ulong value) =>
        type.SetUINT64(ref key, value).Throw("SetUINT64");

    private static ulong PackSize(int width, int height) => ((ulong)width << 32) | (uint)height;

    public void Dispose()
    {
        if (!_finalized)
        {
            StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        try { _item.Closed -= OnTargetClosed; } catch { }
        D3D11Helpers.Release(_staging);
        D3D11Helpers.Release(_d3dContext);
        D3D11Helpers.Release(_d3dDevice);
        if (_sinkWriter is not null && Marshal.IsComObject(_sinkWriter))
        {
            Marshal.ReleaseComObject(_sinkWriter);
        }
    }
}

/// <summary>Fluent HRESULT check for the many PreserveSig returns.</summary>
internal static class HResultExtensions
{
    public static void Throw(this int hr, string operation) => HResult.ThrowIfFailed(hr, operation);
}
