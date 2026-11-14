using System;
using MaxTweaks.Core.HeartRateGraph;
using Xunit;

namespace MaxTweaks.Core.Tests.HeartRateGraph;

public class PlasmaTests
{
    [Theory]
    [InlineData(0.0, 0x0d, 0x08, 0x87)]
    [InlineData(1.0, 0xf0, 0xf9, 0x21)]
    [InlineData(-3.0, 0x0d, 0x08, 0x87)]
    [InlineData(7.0, 0xf0, 0xf9, 0x21)]
    [InlineData(double.NaN, 0x0d, 0x08, 0x87)]
    public void Rgb_EndsAndClamping(double t, byte r, byte g, byte b)
    {
        Plasma.Rgb(t, out var red, out var green, out var blue);
        Assert.Equal((r, g, b), (red, green, blue));
    }

    [Fact]
    public void Rgb_GoesFromBlueThroughMagentaToYellow()
    {
        Plasma.Rgb(0.5, out var r, out var g, out var b);
        // Middle of the ramp is a magenta-pink (about #cc4778).
        Assert.InRange(r, 0xc6, 0xd2);
        Assert.InRange(g, 0x40, 0x4e);
        Assert.InRange(b, 0x72, 0x7e);

    }

    [Theory]
    [InlineData(0.2, 0x6a, 0x00, 0xa8)] // purple
    [InlineData(0.4, 0xb1, 0x2a, 0x90)] // magenta
    [InlineData(0.6, 0xe1, 0x64, 0x62)] // salmon
    [InlineData(0.8, 0xfc, 0xa6, 0x36)] // orange
    public void Rgb_MatchesTheReferenceTable(double t, byte r, byte g, byte b)
    {
        // Entries 51, 102, 153 and 204 of d3's 256-colour plasma table.
        Plasma.Rgb(t, out var red, out var green, out var blue);
        Assert.Equal((r, g, b), (red, green, blue));
    }

    [Fact]
    public void Rgb_InterpolatesBetweenTableEntries()
    {
        // Entries 0 and 1 are 0d0887 and 100788: halfway between them.
        Plasma.Rgb(0.5 / (Plasma.Count - 1), out var r, out var g, out var b);
        Assert.InRange(r, 0x0e, 0x0f);
        Assert.InRange(g, 0x07, 0x08);
        Assert.InRange(b, 0x87, 0x88);
    }

    [Fact]
    public void Lut_IsOpaqueAndRunsLowToHigh()
    {
        var lut = Plasma.Lut(64);
        Assert.Equal(64 * 4, lut.Length);
        Assert.Equal(new byte[] { 0x0d, 0x08, 0x87, 255 }, lut[..4]);
        Assert.Equal(new byte[] { 0xf0, 0xf9, 0x21, 255 }, lut[^4..]);
        for (var i = 3; i < lut.Length; i += 4)
        {
            Assert.Equal(255, lut[i]);
        }
    }

    [Fact]
    public void Lut_NeedsTwoPixels()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Plasma.Lut(1));
    }
}
