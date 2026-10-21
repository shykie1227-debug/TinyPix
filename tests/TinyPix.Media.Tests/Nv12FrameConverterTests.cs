using System;
using TinyPix.Media.Capture;
using Xunit;

namespace TinyPix.Media.Tests;

public sealed class Nv12FrameConverterTests
{
    [Fact]
    public void Convert_SolidWhite_ProducesStudioRangeLumaAndNeutralChroma()
    {
        int width = 4, height = 4;
        var bgra = new byte[width * height * 4];
        for (int i = 0; i < bgra.Length; i += 4)
        {
            bgra[i] = 255;
            bgra[i + 1] = 255;
            bgra[i + 2] = 255;
            bgra[i + 3] = 255;
        }

        var nv12 = new byte[width * height * 3 / 2];
        Nv12FrameConverter.ConvertBgraToNv12(bgra, width, height, 0, 0, width, height, nv12, width, height);

        // BT.601 limited range: white → Y=235, U=V=128.
        Assert.Equal(235, nv12[0]);
        Assert.Equal(235, nv12[width * height - 1]);
        Assert.Equal(128, nv12[width * height]);
        Assert.Equal(128, nv12[width * height + 1]);
    }

    [Fact]
    public void Convert_SolidBlack_ProducesLuma16()
    {
        int width = 2, height = 2;
        var nv12 = new byte[width * height * 3 / 2];
        Nv12FrameConverter.ConvertBgraToNv12(
            new byte[width * height * 4], width, height, 0, 0, width, height, nv12, width, height);
        Assert.Equal(16, nv12[0]);
    }

    [Fact]
    public void Convert_CropReadsOnlyTheCropRect()
    {
        // 4×4 source: crop the right-bottom 2×2 quadrant filled with white,
        // while the rest is black. Output 2×2 must be pure white (Y=235).
        int width = 4, height = 4;
        var bgra = new byte[width * height * 4];
        for (int y = 2; y < 4; y++)
        {
            for (int x = 2; x < 4; x++)
            {
                int i = (y * width + x) * 4;
                bgra[i] = 255;
                bgra[i + 1] = 255;
                bgra[i + 2] = 255;
                bgra[i + 3] = 255;
            }
        }

        var nv12 = new byte[2 * 2 * 3 / 2];
        Nv12FrameConverter.ConvertBgraToNv12(bgra, width, height, 2, 2, 2, 2, nv12, 2, 2);
        Assert.Equal(235, nv12[0]);
        Assert.Equal(235, nv12[3]);
    }

    [Fact]
    public void Convert_DownscaleAveragesSourceBlocks()
    {
        // 4×2 source with alternating black/white columns; downscale to 2×2 →
        // each output pixel averages one black and one white source pixel → grey.
        int width = 4, height = 2;
        var bgra = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if ((x & 1) == 1)
                {
                    int i = (y * width + x) * 4;
                    bgra[i] = 255;
                    bgra[i + 1] = 255;
                    bgra[i + 2] = 255;
                    bgra[i + 3] = 255;
                }
            }
        }

        var nv12 = new byte[2 * 2 * 3 / 2];
        Nv12FrameConverter.ConvertBgraToNv12(bgra, width, height, 0, 0, width, height, nv12, 2, 2);

        // 50% grey (127) in BT.601 limited range lands at Y=125.
        Assert.Equal(125, nv12[0]);
        Assert.Equal(125, nv12[1]);
        Assert.Equal(125, nv12[2]);
        Assert.Equal(125, nv12[3]);
    }

    [Fact]
    public void Convert_RejectsOddTargetsAndBadCrops()
    {
        var bgra = new byte[4 * 4 * 4];
        var nv12 = new byte[4 * 4 * 3 / 2];
        Assert.Throws<ArgumentException>(() => Nv12FrameConverter.ConvertBgraToNv12(
            bgra, 4, 4, 0, 0, 4, 4, nv12, 3, 4));
        Assert.Throws<ArgumentException>(() => Nv12FrameConverter.ConvertBgraToNv12(
            bgra, 4, 4, 3, 3, 4, 4, nv12, 4, 4));
        Assert.Throws<ArgumentException>(() => Nv12FrameConverter.ConvertBgraToNv12(
            bgra, 4, 4, 0, 0, 4, 4, new byte[10], 4, 4));
    }
}
