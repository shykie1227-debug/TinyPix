using System;
using System.Collections.Generic;
using System.Globalization;

namespace TinyPix.Media.Ffmpeg;

/// <summary>
/// LGPL-only video encoder selection shared by every FFmpeg handler.
///
/// <c>design/DEPENDENCY-BASELINE.md</c> §6 and <c>LOCAL_RULES.md</c> §5 freeze
/// TinyPix to an LGPL FFmpeg build: any occurrence of <c>libx264</c>,
/// <c>libx265</c> or <c>libfdk-aac</c> must fail review because those encoders
/// pull GPL or nonfree licenses into the redistributed package. Handlers must
/// therefore ask this helper for a codec and its quality arguments instead of
/// hardcoding an encoder name.
/// </summary>
public static class LgplVideoCodec
{
    /// <summary>
    /// Cisco OpenH264. LGPL-clean and bundled by every LGPL Windows build we ship.
    /// It has no CRF equivalent, so quality is expressed as a target bitrate.
    /// </summary>
    public const string H264 = "libopenh264";

    /// <summary>VP8 for WebM. Supports CRF together with <c>-b:v 0</c>.</summary>
    public const string Vp8 = "libvpx";

    /// <summary>Native MPEG-4 Part 2. Widest compatibility fallback.</summary>
    public const string Mpeg4 = "mpeg4";

    /// <summary>Default quality used when a handler has no user-facing slider.</summary>
    public const int DefaultQuality = 80;

    /// <summary>Pick the LGPL encoder that matches the requested container.</summary>
    public static string ForContainer(string format) =>
        string.Equals(format, "webm", StringComparison.OrdinalIgnoreCase) ? Vp8 : H264;

    /// <summary>True for the encoders that accept a constant-rate-factor argument.</summary>
    public static bool SupportsCrf(string codec) => codec is Vp8;

    /// <summary>
    /// Clamped CRF for CRF-capable LGPL encoders: 18 is the best quality,
    /// 28 the leanest. Matches the historical quality slider mapping.
    /// </summary>
    public static int CrfForQuality(int quality) =>
        Math.Clamp(28 - (int)Math.Round(Math.Clamp(quality, 0, 100) * 0.10), 18, 28);

    /// <summary>
    /// Target bitrate in kbps for bitrate-driven LGPL encoders.
    /// quality 0 -> 300k, quality 80 -> 8300k, quality 100 -> 10300k.
    /// </summary>
    public static int BitrateKbpsForQuality(int quality) =>
        300 + (Math.Clamp(quality, 0, 100) * 100);

    /// <summary>
    /// Builds the encoder switch plus its quality arguments for the resolved
    /// LGPL codec. Returns <c>-c:v &lt;codec&gt;</c> followed by either
    /// <c>-crf N -b:v 0</c> or <c>-b:v NNNNk</c>.
    /// </summary>
    public static IReadOnlyList<string> EncoderArguments(string codec, int quality)
    {
        var args = new List<string> { "-c:v", codec };

        if (SupportsCrf(codec))
        {
            args.Add("-crf");
            args.Add(CrfForQuality(quality).ToString(CultureInfo.InvariantCulture));
            args.Add("-b:v");
            args.Add("0");
        }
        else
        {
            args.Add("-b:v");
            args.Add($"{BitrateKbpsForQuality(quality).ToString(CultureInfo.InvariantCulture)}k");
        }

        return args;
    }

    /// <summary>
    /// Convenience overload for the edit handlers, which always re-encode to H.264
    /// with the default quality instead of exposing a slider.
    /// </summary>
    public static IReadOnlyList<string> H264Arguments() =>
        EncoderArguments(H264, DefaultQuality);
}
