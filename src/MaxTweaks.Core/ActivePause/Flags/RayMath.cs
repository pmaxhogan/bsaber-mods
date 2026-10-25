using System;

namespace MaxTweaks.Core.ActivePause.Flags;

/// <summary>Ray tests for picking frozen notes with a saber ray (plain floats, no Unity types).</summary>
public static class RayMath
{
    /// <summary>
    /// Where the ray from (o) along the unit direction (d) first meets the sphere around (c).
    /// <paramref name="distance"/> is the distance along the ray to the entry point, or 0 when the
    /// origin is inside the sphere. False for a miss, a sphere entirely behind the origin, or a
    /// zero direction.
    /// </summary>
    public static bool RaySphere(
        float ox, float oy, float oz,
        float dx, float dy, float dz,
        float cx, float cy, float cz,
        float radius,
        out float distance)
    {
        distance = 0f;
        var dd = (dx * dx) + (dy * dy) + (dz * dz);
        if (!(dd > 1e-12f) || !(radius > 0f))
        {
            return false;
        }

        // Work with a normalized direction so the distance is in world units.
        var inv = 1.0 / Math.Sqrt(dd);
        double ux = dx * inv, uy = dy * inv, uz = dz * inv;
        double lx = cx - ox, ly = cy - oy, lz = cz - oz;
        var r2 = (double)radius * radius;
        var l2 = (lx * lx) + (ly * ly) + (lz * lz);
        if (l2 <= r2)
        {
            return true; // origin inside
        }

        var along = (lx * ux) + (ly * uy) + (lz * uz);
        if (along < 0)
        {
            return false; // centre behind the origin, origin outside
        }

        var perp2 = l2 - (along * along);
        if (perp2 > r2)
        {
            return false;
        }

        distance = (float)(along - Math.Sqrt(r2 - perp2));
        return true;
    }
}
