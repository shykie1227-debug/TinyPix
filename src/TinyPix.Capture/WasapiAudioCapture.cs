using System;
using System.Runtime.InteropServices;
using System.Threading;
using TinyPix.Capture.Interop;

namespace TinyPix.Capture;

/// <summary>
/// Single-device WASAPI capture source (E1 audio side of capture.record).
/// Captures the shared-mode mix format (float32) from either the render
/// endpoint (loopback = 系统音频) or the capture endpoint (麦克风), reporting
/// packets with their absolute device frame position so the mixer can insert
/// silence for gaps (loopback is silent — i.e. delivers nothing — when no app
/// is playing audio).
/// </summary>
internal sealed class WasapiCaptureSource : IDisposable
{
    private readonly bool _loopback;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _stopped = new(false);
    private volatile bool _quit;

    private NativeMethods.IMMDeviceEnumerator? _enumerator;
    private NativeMethods.IMMDevice? _device;
    private NativeMethods.IAudioClient? _client;
    private NativeMethods.IAudioCaptureClient? _capture;

    private int _channels;
    private int _sampleRate;

    /// <summary>startFrame (device position), interleaved float samples, frames.</summary>
    public event Action<long, float[], int>? PacketCaptured;

    private WasapiCaptureSource(bool loopback)
    {
        _loopback = loopback;
        _thread = new Thread(CaptureLoop)
        {
            IsBackground = true,
            Name = loopback ? "tinypix-wasapi-loopback" : "tinypix-wasapi-mic",
        };
    }

    public int SampleRate => _sampleRate;

    public int Channels => _channels;

    public static WasapiCaptureSource Start(bool loopback)
    {
        var source = new WasapiCaptureSource(loopback);
        source.Initialize();
        source._thread.Start();
        return source;
    }

    private void Initialize()
    {
        Guid clsid = new("bcde0395-e52f-467c-8e3d-c4579291692e");
        Guid iidEnumerator = new("a95664d2-9614-4f35-a746-de8db63617e6");
        HResult.ThrowIfFailed(
            NativeMethods.CoCreateInstance(
                ref clsid, IntPtr.Zero, NativeMethods.CLSCTX_ALL, ref iidEnumerator, out IntPtr enumeratorPtr),
            "CoCreateInstance(MMDeviceEnumerator)");
        _enumerator = (NativeMethods.IMMDeviceEnumerator)Marshal.GetObjectForIUnknown(enumeratorPtr);
        Marshal.Release(enumeratorPtr);

        int dataFlow = _loopback ? NativeMethods.eRender : NativeMethods.eCapture;
        HResult.ThrowIfFailed(
            _enumerator.GetDefaultAudioEndpoint(dataFlow, NativeMethods.eConsole, out IntPtr devicePtr),
            "GetDefaultAudioEndpoint");
        _device = (NativeMethods.IMMDevice)Marshal.GetObjectForIUnknown(devicePtr);
        Marshal.Release(devicePtr);

        Guid iidAudioClient = new("1cb9ad4c-dbfa-4c32-b178-c2f568a703b2");
        HResult.ThrowIfFailed(
            _device.Activate(ref iidAudioClient, NativeMethods.CLSCTX_ALL, IntPtr.Zero, out object clientObj),
            "Activate(IAudioClient)");
        _client = (NativeMethods.IAudioClient)clientObj;

        HResult.ThrowIfFailed(_client.GetMixFormat(out IntPtr formatPtr), "GetMixFormat");
        try
        {
            var format = Marshal.PtrToStructure<NativeMethods.WaveFormatEx>(formatPtr);
            _channels = format.Channels;
            _sampleRate = (int)format.SamplesPerSec;
        }
        finally
        {
            Marshal.FreeCoTaskMem(formatPtr);
        }

        uint flags = _loopback ? NativeMethods.AUDCLNT_STREAMFLAGS_LOOPBACK : 0;
        long bufferDuration = 200 * NativeMethods.REFTIMES_PER_MILLISEC;
        HResult.ThrowIfFailed(
            _client.Initialize(
                NativeMethods.AUDCLNT_SHAREMODE_SHARED,
                flags,
                bufferDuration,
                0,
                IntPtr.Zero,
                IntPtr.Zero),
            "IAudioClient.Initialize");

        Guid iidCaptureClient = new("c8adbd64-e71e-48a0-a4de-185c395cd317");
        HResult.ThrowIfFailed(
            _client.GetService(ref iidCaptureClient, out IntPtr capturePtr),
            "GetService(IAudioCaptureClient)");
        _capture = (NativeMethods.IAudioCaptureClient)Marshal.GetObjectForIUnknown(capturePtr);
        Marshal.Release(capturePtr);

        HResult.ThrowIfFailed(_client.Start(), "IAudioClient.Start");
    }

    private void CaptureLoop()
    {
        try
        {
            while (!_quit)
            {
                HResult.ThrowIfFailed(_capture!.GetNextPacketSize(out uint packetFrames), "GetNextPacketSize");
                if (packetFrames == 0)
                {
                    _stopped.Wait(10);
                    continue;
                }

                while (packetFrames > 0 && !_quit)
                {
                    HResult.ThrowIfFailed(
                        _capture.GetBuffer(
                            out IntPtr data,
                            out uint frames,
                            out _,
                            out ulong devicePosition,
                            out _),
                        "GetBuffer");
                    var interleaved = new float[frames * _channels];
                    Marshal.Copy(data, interleaved, 0, interleaved.Length);
                    HResult.ThrowIfFailed(_capture.ReleaseBuffer(frames), "ReleaseBuffer");
                    PacketCaptured?.Invoke((long)devicePosition, interleaved, (int)frames);

                    HResult.ThrowIfFailed(_capture.GetNextPacketSize(out packetFrames), "GetNextPacketSize");
                }
            }
        }
        catch
        {
            // Capture errors surface through missing packets; the session owns
            // the error surface for the user (device lost etc.).
        }
    }

    public void Dispose()
    {
        _quit = true;
        if (_thread.IsAlive)
        {
            _thread.Join(500);
        }

        try { _client?.Stop(); } catch { }
        if (_capture is not null && Marshal.IsComObject(_capture)) { Marshal.ReleaseComObject(_capture); }
        if (_client is not null && Marshal.IsComObject(_client)) { Marshal.ReleaseComObject(_client); }
        if (_device is not null && Marshal.IsComObject(_device)) { Marshal.ReleaseComObject(_device); }
        if (_enumerator is not null && Marshal.IsComObject(_enumerator)) { Marshal.ReleaseComObject(_enumerator); }
        _stopped.Dispose();
    }
}

/// <summary>
/// Mixes one or two capture sources into a continuous PCM16 stream at
/// 48 kHz, inserting silence wherever a source has no packets (loopback gaps).
/// Frames are pulled by absolute device position so sources stay aligned.
/// </summary>
internal sealed class PcmAudioMixer : IDisposable
{
    private readonly object _gate = new();
    private readonly System.Collections.Generic.Dictionary<long, float[]> _pendingFrames = new();
    private readonly int _sampleRate;
    private readonly int _channels;
    private long _nextFrame;
    private long _highestSeen;

    public PcmAudioMixer(int sampleRate, int channels)
    {
        _sampleRate = sampleRate;
        _channels = channels;
    }

    public long NextFrame => Interlocked.Read(ref _nextFrame);

    public void Submit(long startFrame, float[] interleaved, int frames)
    {
        lock (_gate)
        {
            for (int i = 0; i < frames; i++)
            {
                long frame = startFrame + i;
                if (!_pendingFrames.TryGetValue(frame, out float[]? slot))
                {
                    slot = new float[_channels];
                    _pendingFrames[frame] = slot;
                }

                for (int c = 0; c < _channels && c < interleaved.Length / frames; c++)
                {
                    slot[c] += interleaved[i * (interleaved.Length / frames) + c];
                }
            }

            if (startFrame + frames > _highestSeen)
            {
                _highestSeen = startFrame + frames;
            }
        }
    }

    /// <summary>
    /// Pull up to <paramref name="maxFrames"/> ready frames as PCM16.
    /// "Ready" means captured — silence fills gaps automatically because
    /// missing frame slots stay zero.
    /// </summary>
    public byte[] PullPcm16(int maxFrames, out int frames, bool flush)
    {
        lock (_gate)
        {
            long available = flush ? _highestSeen - _nextFrame : Math.Min(_highestSeen, _nextFrame + maxFrames) - _nextFrame;
            frames = (int)Math.Clamp(available, 0, maxFrames);
            var pcm = new byte[frames * _channels * 2];
            for (int i = 0; i < frames; i++)
            {
                long frame = _nextFrame + i;
                _pendingFrames.TryGetValue(frame, out float[]? slot);
                for (int c = 0; c < _channels; c++)
                {
                    float sample = slot is not null && c < slot.Length ? slot[c] : 0f;
                    short value = (short)Math.Clamp((int)(sample * 32767f), short.MinValue, short.MaxValue);
                    pcm[(i * _channels + c) * 2] = (byte)(value & 0xff);
                    pcm[(i * _channels + c) * 2 + 1] = (byte)((value >> 8) & 0xff);
                }
            }

            _nextFrame += frames;

            // Drop frame slots we have consumed or that are hopelessly stale.
            if (_pendingFrames.Count > _sampleRate)
            {
                foreach (long frame in new System.Collections.Generic.List<long>(_pendingFrames.Keys))
                {
                    if (frame < _nextFrame)
                    {
                        _pendingFrames.Remove(frame);
                    }
                }
            }

            return pcm;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _pendingFrames.Clear();
        }
    }
}
