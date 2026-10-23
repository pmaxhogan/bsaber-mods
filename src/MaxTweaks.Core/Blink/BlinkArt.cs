using System;

namespace MaxTweaks.Core.Blink;

/// <summary>
/// Procedural art for the blink overlay (no shipped image assets). Pure per-pixel functions; the
/// plugin fills Unity textures from them once.
/// </summary>
public static class BlinkArt
{
    /// <summary>
    /// Half the vignette quad's angular size seen from the eye. The quad extends to
    /// tan(QuadHalfAngle) * distance in every direction, which covers every current HMD's field of view.
    /// </summary>
    public const float QuadHalfAngleDegrees = 70f;

    /// <summary>Inside this angle from the view centre the vignette is fully clear.</summary>
    public const float ClearAngleDegrees = 20f;

    /// <summary>At and beyond this angle (about the visible corners in an HMD) it reaches full strength.</summary>
    public const float FullAngleDegrees = 55f;

    public static float SmoothStep(float edge0, float edge1, float x)
    {
        if (edge1 <= edge0)
        {
            return x < edge0 ? 0f : 1f;
        }

        var t = Clamp01((x - edge0) / (edge1 - edge0));
        return t * t * (3f - (2f * t));
    }

    /// <summary>Normalised quad radius (0 = centre, 1 = quad edge midpoint) seen at the given angle.</summary>
    public static float RadiusForAngle(float degrees)
    {
        return (float)(Math.Tan(degrees * Math.PI / 180.0) / Math.Tan(QuadHalfAngleDegrees * Math.PI / 180.0));
    }

    /// <summary>
    /// Vignette opacity mask (before strength) at a point of the quad; x and y in [-1, 1] from the centre.
    /// 0 in the clear centre, rising smoothly to 1 towards the corners of the view.
    /// </summary>
    public static float VignetteMask(float x, float y)
    {
        var r = (float)Math.Sqrt((x * x) + (y * y));
        return SmoothStep(RadiusForAngle(ClearAngleDegrees), RadiusForAngle(FullAngleDegrees), r);
    }

    /// <summary>
    /// Closed-eye icon coverage at a point; x and y in [-1, 1] from the icon centre, y up.
    /// A downward lid arc with five short lashes below it, anti-aliased over <paramref name="pixel"/>
    /// (the size of one texel in the same units).
    /// </summary>
    public static float ClosedEye(float x, float y, float pixel)
    {
        const float stroke = 0.075f;
        var d = LidDistance(x, y);

        // Lashes: short strokes leaving the lid along its outward (downward) normal.
        for (var i = -2; i <= 2; i++)
        {
            var lx = i * 0.3f;
            var ly = LidY(lx);
            var slope = LidSlope(lx);

            // Outward normal of y = f(x) pointing down: (slope, -1) normalised.
            var len = (float)Math.Sqrt((slope * slope) + 1f);
            var nx = slope / len;
            var ny = -1f / len;
            var start = 0.05f;
            var end = 0.3f;
            d = Math.Min(d, SegmentDistance(x, y, lx + (nx * start), ly + (ny * start), lx + (nx * end), ly + (ny * end)));
        }

        var aa = Math.Max(pixel, 1e-4f);
        return Clamp01(((stroke - d) / aa) + 0.5f);
    }

    private const float LidHalfWidth = 0.8f;

    private static float LidY(float x)
    {
        var t = x / LidHalfWidth;
        return 0.3f - (0.5f * (1f - (t * t)));
    }

    private static float LidSlope(float x) => x / (LidHalfWidth * LidHalfWidth);

    private static float LidDistance(float x, float y)
    {
        // Sample the arc as a polyline; 16 segments is smooth at icon sizes.
        const int segments = 16;
        var best = float.MaxValue;
        var px = -LidHalfWidth;
        var py = LidY(px);
        for (var i = 1; i <= segments; i++)
        {
            var qx = -LidHalfWidth + (2f * LidHalfWidth * i / segments);
            var qy = LidY(qx);
            best = Math.Min(best, SegmentDistance(x, y, px, py, qx, qy));
            px = qx;
            py = qy;
        }

        return best;
    }

    /// <summary>Distance from (x, y) to the segment a-b.</summary>
    public static float SegmentDistance(float x, float y, float ax, float ay, float bx, float by)
    {
        var dx = bx - ax;
        var dy = by - ay;
        var lengthSq = (dx * dx) + (dy * dy);
        var t = lengthSq > 0f ? Clamp01((((x - ax) * dx) + ((y - ay) * dy)) / lengthSq) : 0f;
        var cx = ax + (t * dx) - x;
        var cy = ay + (t * dy) - y;
        return (float)Math.Sqrt((cx * cx) + (cy * cy));
    }

    private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
}
