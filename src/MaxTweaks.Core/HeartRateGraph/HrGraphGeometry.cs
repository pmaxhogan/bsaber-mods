using System;
using System.Collections.Generic;

namespace MaxTweaks.Core.HeartRateGraph;

/// <summary>Where and how big the graph is drawn. Lengths are in the caller's units (canvas units).</summary>
public sealed class HrGraphLayout
{
    /// <summary>Song length in seconds: the right edge of the graph.</summary>
    public double SongLength { get; set; }

    /// <summary>Heart rate on the bottom edge.</summary>
    public int MinBpm { get; set; } = 100;

    /// <summary>Heart rate on the top edge.</summary>
    public int MaxBpm { get; set; } = 200;

    /// <summary>Heart rate that gets the first colour of the ramp (and everything below it); null = MinBpm.</summary>
    public int? ColorMinBpm { get; set; }

    /// <summary>Heart rate that gets the last colour of the ramp (and everything above it); null = MaxBpm.</summary>
    public int? ColorMaxBpm { get; set; }

    public float Width { get; set; }

    public float Height { get; set; }

    public float LineWidth { get; set; }

    /// <summary>Opacity of the fill on the bottom edge.</summary>
    public float FillAlphaBottom { get; set; } = 0.32f;

    /// <summary>Opacity the fill reaches on the top edge; in between it follows the height.</summary>
    public float FillAlphaTop { get; set; } = 1f;

    public float LineAlpha { get; set; } = 1f;

    /// <summary>
    /// Opacity of the level line from the newest sample to the right edge: where the graph would go
    /// if the heart rate stayed exactly as it is for the rest of the song. 0 = not drawn.
    /// </summary>
    public float ProjectionAlpha { get; set; } = 0.75f;
}

/// <summary>
/// One corner of the graph mesh. X/Y start at the graph's bottom left corner. Value is where the
/// heart rate at that height falls on the colour ramp (0 = ColorMinBpm, 1 = ColorMaxBpm): the ramp
/// is looked up with it, so every pixel takes the colour of the heart rate it stands for. It is not
/// clamped: below 0 and above 1 mean the ramp's end colours, which keeps it a straight-line function
/// of the height (what a triangle can interpolate exactly) when the colour range is narrower than the axis.
/// </summary>
public readonly struct HrGraphVertex
{
    public HrGraphVertex(float x, float y, float value, float alpha)
    {
        X = x;
        Y = y;
        Value = value;
        Alpha = alpha;
    }

    public float X { get; }

    public float Y { get; }

    public float Value { get; }

    public float Alpha { get; }
}

public sealed class HrGraphMesh
{
    public List<HrGraphVertex> Vertices { get; } = new();

    /// <summary>Vertex indices, three per triangle, in drawing order (fill first, line on top).</summary>
    public List<int> Triangles { get; } = new();

    public void Clear()
    {
        Vertices.Clear();
        Triangles.Clear();
    }
}

/// <summary>
/// Turns the samples of an <see cref="HrSeries"/> into triangles: per run of joined samples a filled
/// area from the bottom edge up to the heart rate, and on top of it the line itself. X is the share
/// of the song played, Y the heart rate between MinBpm and MaxBpm (clamped to the edges).
/// </summary>
public static class HrGraphGeometry
{
    /// <summary>Sharp corners are not extended further than this multiple of half the line width.</summary>
    private const float MiterLimit = 3f;

    public static void Build(HrGraphMesh mesh, IReadOnlyList<HrSample> samples, HrGraphLayout layout)
    {
        mesh.Clear();
        var count = samples.Count;
        if (count == 0 || !(layout.SongLength > 0) || layout.MaxBpm <= layout.MinBpm || !(layout.Width > 0f) || !(layout.Height > 0f))
        {
            return;
        }

        var xs = new float[count];
        var ys = new float[count];
        var values = new float[count];
        var range = (double)(layout.MaxBpm - layout.MinBpm);
        var colorMin = layout.ColorMinBpm ?? layout.MinBpm;
        var colorMax = layout.ColorMaxBpm ?? layout.MaxBpm;
        if (colorMax <= colorMin)
        {
            colorMin = layout.MinBpm;
            colorMax = layout.MaxBpm;
        }

        // Colour coordinate of the bottom edge and how much it grows over the full height.
        var bottomValue = (float)((layout.MinBpm - colorMin) / (double)(colorMax - colorMin));
        var valueSpan = (float)(range / (colorMax - colorMin));
        var heights = new float[count];
        for (var i = 0; i < count; i++)
        {
            var height = (float)Clamp01((samples[i].Bpm - layout.MinBpm) / range);
            xs[i] = (float)Clamp01(samples[i].Time / layout.SongLength) * layout.Width;
            ys[i] = height * layout.Height;
            heights[i] = height;
            values[i] = bottomValue + (height * valueSpan);
        }

        for (var start = 0; start < count;)
        {
            var end = RunEnd(samples, start);
            AddFill(mesh, xs, ys, values, heights, bottomValue, start, end, layout);
            start = end;
        }

        if (!(layout.LineWidth > 0f))
        {
            return;
        }

        for (var start = 0; start < count;)
        {
            var end = RunEnd(samples, start);
            if (end - start == 1)
            {
                AddDot(mesh, xs[start], ys[start], values[start], layout);
            }
            else
            {
                AddLine(mesh, xs, ys, values, start, end, layout);
            }

            start = end;
        }

        AddProjection(mesh, xs[count - 1], ys[count - 1], values[count - 1], layout);
    }

    private static void AddProjection(HrGraphMesh mesh, float x, float y, float value, HrGraphLayout layout)
    {
        if (!(layout.ProjectionAlpha > 0f) || x >= layout.Width)
        {
            return;
        }

        var half = layout.LineWidth * 0.5f;
        var first = mesh.Vertices.Count;
        mesh.Vertices.Add(new HrGraphVertex(x, y - half, value, layout.ProjectionAlpha));
        mesh.Vertices.Add(new HrGraphVertex(x, y + half, value, layout.ProjectionAlpha));
        mesh.Vertices.Add(new HrGraphVertex(layout.Width, y - half, value, layout.ProjectionAlpha));
        mesh.Vertices.Add(new HrGraphVertex(layout.Width, y + half, value, layout.ProjectionAlpha));
        AddStrip(mesh, first, 2);
    }

    private static int RunEnd(IReadOnlyList<HrSample> samples, int start)
    {
        var end = start + 1;
        while (end < samples.Count && !samples[end].StartsRun)
        {
            end++;
        }

        return end;
    }

    private static void AddFill(
        HrGraphMesh mesh, float[] xs, float[] ys, float[] values, float[] heights, float bottomValue, int start, int end, HrGraphLayout layout)
    {
        if (end - start < 2)
        {
            return;
        }

        var first = mesh.Vertices.Count;
        for (var i = start; i < end; i++)
        {
            var topAlpha = layout.FillAlphaBottom + ((layout.FillAlphaTop - layout.FillAlphaBottom) * heights[i]);
            mesh.Vertices.Add(new HrGraphVertex(xs[i], 0f, bottomValue, layout.FillAlphaBottom));
            mesh.Vertices.Add(new HrGraphVertex(xs[i], ys[i], values[i], topAlpha));
        }

        AddStrip(mesh, first, end - start);
    }

    private static void AddLine(HrGraphMesh mesh, float[] xs, float[] ys, float[] values, int start, int end, HrGraphLayout layout)
    {
        var half = layout.LineWidth * 0.5f;
        var first = mesh.Vertices.Count;
        for (var i = start; i < end; i++)
        {
            // Offset each point along the average of its two segment normals (a miter joint).
            float nx;
            float ny;
            var scale = 1f;
            if (i == start)
            {
                Normal(xs[i], ys[i], xs[i + 1], ys[i + 1], out nx, out ny);
            }
            else if (i == end - 1)
            {
                Normal(xs[i - 1], ys[i - 1], xs[i], ys[i], out nx, out ny);
            }
            else
            {
                Normal(xs[i - 1], ys[i - 1], xs[i], ys[i], out var ax, out var ay);
                Normal(xs[i], ys[i], xs[i + 1], ys[i + 1], out var bx, out var by);
                nx = ax + bx;
                ny = ay + by;
                var length = (float)Math.Sqrt((nx * nx) + (ny * ny));
                if (length < 1e-4f)
                {
                    nx = ax;
                    ny = ay;
                }
                else
                {
                    nx /= length;
                    ny /= length;
                    var cos = (nx * ax) + (ny * ay);
                    scale = Math.Min(MiterLimit, 1f / Math.Max(cos, 1e-3f));
                }
            }

            var offset = half * scale;
            mesh.Vertices.Add(new HrGraphVertex(xs[i] - (nx * offset), ys[i] - (ny * offset), values[i], layout.LineAlpha));
            mesh.Vertices.Add(new HrGraphVertex(xs[i] + (nx * offset), ys[i] + (ny * offset), values[i], layout.LineAlpha));
        }

        AddStrip(mesh, first, end - start);
    }

    private static void AddDot(HrGraphMesh mesh, float x, float y, float value, HrGraphLayout layout)
    {
        var half = layout.LineWidth * 0.5f;
        var first = mesh.Vertices.Count;
        mesh.Vertices.Add(new HrGraphVertex(x - half, y - half, value, layout.LineAlpha));
        mesh.Vertices.Add(new HrGraphVertex(x - half, y + half, value, layout.LineAlpha));
        mesh.Vertices.Add(new HrGraphVertex(x + half, y - half, value, layout.LineAlpha));
        mesh.Vertices.Add(new HrGraphVertex(x + half, y + half, value, layout.LineAlpha));
        AddStrip(mesh, first, 2);
    }

    /// <summary>Triangles for <paramref name="pairs"/> vertex pairs (lower, upper) laid out left to right.</summary>
    private static void AddStrip(HrGraphMesh mesh, int first, int pairs)
    {
        for (var i = 0; i < pairs - 1; i++)
        {
            var at = first + (i * 2);
            mesh.Triangles.Add(at);
            mesh.Triangles.Add(at + 1);
            mesh.Triangles.Add(at + 3);
            mesh.Triangles.Add(at);
            mesh.Triangles.Add(at + 3);
            mesh.Triangles.Add(at + 2);
        }
    }

    /// <summary>Unit normal (pointing up for a left-to-right segment); straight up for a zero-length segment.</summary>
    private static void Normal(float x0, float y0, float x1, float y1, out float nx, out float ny)
    {
        var dx = x1 - x0;
        var dy = y1 - y0;
        var length = (float)Math.Sqrt((dx * dx) + (dy * dy));
        if (length < 1e-6f)
        {
            nx = 0f;
            ny = 1f;
            return;
        }

        nx = -dy / length;
        ny = dx / length;
    }

    private static double Clamp01(double value) => !(value > 0) ? 0 : value > 1 ? 1 : value;
}
