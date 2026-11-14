using System.Linq;
using MaxTweaks.Core.HeartRateGraph;
using Xunit;

namespace MaxTweaks.Core.Tests.HeartRateGraph;

public class HrGraphGeometryTests
{
    private static HrGraphLayout Layout() => new()
    {
        SongLength = 100,
        MinBpm = 100,
        MaxBpm = 200,
        Width = 80f,
        Height = 20f,
        LineWidth = 1f,
        FillAlphaBottom = 0.1f,
        FillAlphaTop = 0.6f,
        LineAlpha = 1f,
        ProjectionAlpha = 0f,
    };

    /// <summary>One joined run of samples.</summary>
    private static HrGraphMesh Build(HrGraphLayout layout, params (double Time, int Bpm)[] readings) =>
        BuildRuns(layout, readings.Select((r, i) => new HrSample(r.Time, r.Bpm, i == 0)).ToArray());

    private static HrGraphMesh BuildRuns(HrGraphLayout layout, params HrSample[] samples)
    {
        var mesh = new HrGraphMesh();
        HrGraphGeometry.Build(mesh, samples, layout);
        return mesh;
    }

    private static void AssertIndicesValid(HrGraphMesh mesh)
    {
        Assert.Equal(0, mesh.Triangles.Count % 3);
        Assert.All(mesh.Triangles, index => Assert.InRange(index, 0, mesh.Vertices.Count - 1));
    }

    [Fact]
    public void NoSamples_NoMesh()
    {
        var mesh = Build(Layout());
        Assert.Empty(mesh.Vertices);
        Assert.Empty(mesh.Triangles);
    }

    [Fact]
    public void FlatLine_FillBelowAndLineOnTop()
    {
        // 150 bpm from 25% to 50% of the song.
        var mesh = Build(Layout(), (25, 150), (50, 150));

        Assert.Equal(8, mesh.Vertices.Count);
        Assert.Equal(12, mesh.Triangles.Count);
        AssertIndicesValid(mesh);

        // Fill: bottom and top corner per sample.
        AssertVertex(mesh.Vertices[0], 20f, 0f, 0f, 0.1f);
        AssertVertex(mesh.Vertices[1], 20f, 10f, 0.5f, 0.35f);
        AssertVertex(mesh.Vertices[2], 40f, 0f, 0f, 0.1f);
        AssertVertex(mesh.Vertices[3], 40f, 10f, 0.5f, 0.35f);

        // Line: half the line width below and above each sample, coloured by its heart rate.
        AssertVertex(mesh.Vertices[4], 20f, 9.5f, 0.5f, 1f);
        AssertVertex(mesh.Vertices[5], 20f, 10.5f, 0.5f, 1f);
        AssertVertex(mesh.Vertices[6], 40f, 9.5f, 0.5f, 1f);
        AssertVertex(mesh.Vertices[7], 40f, 10.5f, 0.5f, 1f);

        // The fill is drawn first so the line lands on top.
        Assert.Equal(new[] { 0, 1, 3, 0, 3, 2 }, mesh.Triangles.Take(6));
        Assert.Equal(new[] { 4, 5, 7, 4, 7, 6 }, mesh.Triangles.Skip(6));
    }

    [Fact]
    public void ColourValueFollowsHeight_OnEveryFillCorner()
    {
        var layout = Layout();
        var mesh = Build(layout, (0, 110), (10, 135), (20, 190), (30, 160));
        var fill = mesh.Vertices.Take(8).ToArray();
        Assert.All(fill, v => Assert.Equal(v.Y / layout.Height, v.Value, 5));
        Assert.All(fill, v => Assert.Equal(0.1f + (0.5f * v.Value), v.Alpha, 5));
    }

    [Fact]
    public void HeartRateAndTime_ClampToTheEdges()
    {
        var mesh = Build(Layout(), (-5, 60), (50, 250), (130, 150));
        var fillTops = new[] { mesh.Vertices[1], mesh.Vertices[3], mesh.Vertices[5] };

        AssertVertex(fillTops[0], 0f, 0f, 0f, 0.1f);
        AssertVertex(fillTops[1], 40f, 20f, 1f, 0.6f);
        AssertVertex(fillTops[2], 80f, 10f, 0.5f, 0.35f);
    }

    [Fact]
    public void Hole_SplitsFillAndLine()
    {
        // 10 -> 11 s, a hole, then 30 -> 31 s.
        var mesh = BuildRuns(
            Layout(),
            new HrSample(10, 120, true),
            new HrSample(11, 121, false),
            new HrSample(30, 140, true),
            new HrSample(31, 141, false));

        // Two fills (4 corners each), then two lines (4 corners each); two triangles per piece.
        Assert.Equal(16, mesh.Vertices.Count);
        Assert.Equal(24, mesh.Triangles.Count);
        AssertIndicesValid(mesh);

        // No triangle spans the hole: every triangle stays within one group of four corners.
        for (var i = 0; i < mesh.Triangles.Count; i += 3)
        {
            var groups = mesh.Triangles.Skip(i).Take(3).Select(index => index / 4).Distinct();
            Assert.Single(groups);
        }
    }

    [Fact]
    public void LoneSample_IsADot()
    {
        var mesh = Build(Layout(), (50, 150));

        Assert.Equal(4, mesh.Vertices.Count);
        Assert.Equal(6, mesh.Triangles.Count);
        AssertIndicesValid(mesh);
        Assert.Equal(39.5f, mesh.Vertices.Min(v => v.X), 4);
        Assert.Equal(40.5f, mesh.Vertices.Max(v => v.X), 4);
        Assert.Equal(9.5f, mesh.Vertices.Min(v => v.Y), 4);
        Assert.Equal(10.5f, mesh.Vertices.Max(v => v.Y), 4);
        Assert.All(mesh.Vertices, v => Assert.Equal(0.5f, v.Value));
    }

    [Fact]
    public void Corner_IsMiteredButNotBeyondTheLimit()
    {
        var layout = Layout();

        // A gentle bend: the joint is half a line width (and a little more) from the sample.
        var gentle = Build(layout, (0, 150), (50, 150), (100, 160));
        var joint = gentle.Vertices[6 + 2];
        Assert.InRange(Distance(joint, 40f, 10f), 0.5f, 0.51f);

        // A spike: the joint would shoot far out, so it is cut off at three half widths.
        var spike = Build(layout, (49, 100), (50, 200), (51, 100));
        var tip = spike.Vertices[6 + 2];
        Assert.Equal(1.5f, Distance(tip, 40f, 20f), 3);
    }

    [Fact]
    public void SamplesOnTheSameSpot_StillDraw()
    {
        // Both readings are past the end of the song, so they clamp onto one point.
        var mesh = Build(Layout(), (120, 150), (125, 150));
        AssertIndicesValid(mesh);
        Assert.All(mesh.Vertices, v => Assert.False(float.IsNaN(v.X) || float.IsNaN(v.Y)));
    }

    [Fact]
    public void Projection_RunsLevelFromTheNewestSampleToTheRightEdge()
    {
        var layout = Layout();
        layout.ProjectionAlpha = 0.4f;
        var mesh = Build(layout, (25, 150), (50, 175));

        // Fill (4) + line (4) + projection (4), drawn last.
        Assert.Equal(12, mesh.Vertices.Count);
        Assert.Equal(18, mesh.Triangles.Count);
        AssertIndicesValid(mesh);
        AssertVertex(mesh.Vertices[8], 40f, 14.5f, 0.75f, 0.4f);
        AssertVertex(mesh.Vertices[9], 40f, 15.5f, 0.75f, 0.4f);
        AssertVertex(mesh.Vertices[10], 80f, 14.5f, 0.75f, 0.4f);
        AssertVertex(mesh.Vertices[11], 80f, 15.5f, 0.75f, 0.4f);
    }

    [Fact]
    public void Projection_NotDrawnAtTheEndOfTheSong()
    {
        var layout = Layout();
        layout.ProjectionAlpha = 0.4f;
        var mesh = Build(layout, (50, 150), (100, 150));
        Assert.Equal(8, mesh.Vertices.Count);
    }

    [Fact]
    public void NarrowerColourRange_StretchesTheRampOverTheMiddle()
    {
        // Axis 100..200, colours 115..190: 100 bpm is below the ramp, 200 above it.
        var layout = Layout();
        layout.ColorMinBpm = 115;
        layout.ColorMaxBpm = 190;
        var mesh = Build(layout, (0, 100), (25, 115), (50, 190), (75, 200));

        var bottoms = new[] { mesh.Vertices[0], mesh.Vertices[2], mesh.Vertices[4], mesh.Vertices[6] };
        Assert.All(bottoms, v => Assert.Equal(-0.2f, v.Value, 4));
        Assert.Equal(-0.2f, mesh.Vertices[1].Value, 4);
        Assert.Equal(0f, mesh.Vertices[3].Value, 4);
        Assert.Equal(1f, mesh.Vertices[5].Value, 4);
        Assert.Equal(1.1333f, mesh.Vertices[7].Value, 3);

        // Heights and opacity still follow the 100..200 axis.
        AssertVertex(mesh.Vertices[3], 20f, 3f, 0f, 0.175f);
        AssertVertex(mesh.Vertices[5], 40f, 18f, 1f, 0.55f);
    }

    [Fact]
    public void BackwardsColourRange_FallsBackToTheAxis()
    {
        var layout = Layout();
        layout.ColorMinBpm = 190;
        layout.ColorMaxBpm = 115;
        var mesh = Build(layout, (25, 150), (50, 150));
        Assert.Equal(0.5f, mesh.Vertices[1].Value, 4);
    }

    [Fact]
    public void NoLineWidth_FillOnly()
    {
        var layout = Layout();
        layout.LineWidth = 0f;
        var mesh = Build(layout, (25, 150), (50, 150));
        Assert.Equal(4, mesh.Vertices.Count);
        Assert.Equal(6, mesh.Triangles.Count);
    }

    [Theory]
    [InlineData(0.0, 100, 200, 80f, 20f)]
    [InlineData(double.NaN, 100, 200, 80f, 20f)]
    [InlineData(100.0, 200, 200, 80f, 20f)]
    [InlineData(100.0, 100, 200, 0f, 20f)]
    [InlineData(100.0, 100, 200, 80f, 0f)]
    public void UnusableLayout_NoMesh(double songLength, int minBpm, int maxBpm, float width, float height)
    {
        var layout = Layout();
        layout.SongLength = songLength;
        layout.MinBpm = minBpm;
        layout.MaxBpm = maxBpm;
        layout.Width = width;
        layout.Height = height;

        var mesh = Build(layout, (25, 150), (50, 150));
        Assert.Empty(mesh.Vertices);
        Assert.Empty(mesh.Triangles);
    }

    [Fact]
    public void Rebuild_ReplacesTheOldMesh()
    {
        var samples = new[] { new HrSample(10, 150, true), new HrSample(20, 150, false) };
        var mesh = new HrGraphMesh();
        HrGraphGeometry.Build(mesh, samples, Layout());
        HrGraphGeometry.Build(mesh, samples, Layout());
        Assert.Equal(8, mesh.Vertices.Count);
        Assert.Equal(12, mesh.Triangles.Count);
    }

    private static void AssertVertex(HrGraphVertex v, float x, float y, float value, float alpha)
    {
        Assert.Equal(x, v.X, 4);
        Assert.Equal(y, v.Y, 4);
        Assert.Equal(value, v.Value, 4);
        Assert.Equal(alpha, v.Alpha, 4);
    }

    private static float Distance(HrGraphVertex v, float x, float y) =>
        (float)System.Math.Sqrt(((v.X - x) * (v.X - x)) + ((v.Y - y) * (v.Y - y)));
}
