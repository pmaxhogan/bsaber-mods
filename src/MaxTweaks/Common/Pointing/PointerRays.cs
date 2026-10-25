using UnityEngine;

namespace MaxTweaks.Common.Pointing;

/// <summary>
/// Aiming rays for features that let the player point at world-space buttons and press A: the saber
/// blade (during play), a pause-menu controller, or the camera's forward ray (FPFC desktop mode).
/// Each returns false when the source is missing or hidden.
/// </summary>
internal static class PointerRays
{
    /// <summary>Hits further away than this (metres) are ignored.</summary>
    public const float MaxDistance = 60f;

    /// <summary>Camera forward ray; <paramref name="cache"/> holds Camera.main between calls.</summary>
    public static bool TryFromCamera(ref Camera? cache, out Ray ray)
    {
        if (cache == null)
        {
            cache = Camera.main;
            if (cache == null)
            {
                ray = default;
                return false;
            }
        }

        var t = cache.transform;
        ray = new Ray(t.position, t.forward);
        return true;
    }

    public static bool TryFromController(VRController? controller, out Ray ray)
    {
        if (controller == null || !controller.isActiveAndEnabled)
        {
            ray = default;
            return false;
        }

        var t = controller.transform;
        ray = new Ray(t.position, t.forward);
        return true;
    }

    /// <summary>Ray along the blade, from its bottom towards its tip. False while the saber is hidden.</summary>
    public static bool TryFromSaber(Saber? saber, out Ray ray)
    {
        if (saber == null || !saber.isActiveAndEnabled)
        {
            ray = default;
            return false;
        }

        var bottom = saber.saberBladeBottomPos;
        var direction = saber.saberBladeTopPos - bottom;
        if (direction.sqrMagnitude < 1e-6f)
        {
            ray = default;
            return false;
        }

        ray = new Ray(bottom, direction.normalized);
        return true;
    }

    /// <summary>Where <paramref name="ray"/> meets the plane of <paramref name="screen"/> (within <see cref="MaxDistance"/>, in front of the ray).</summary>
    public static bool TryHitPlane(Transform screen, Ray ray, out Vector3 point)
    {
        var plane = new Plane(screen.forward, screen.position);
        if (!plane.Raycast(ray, out var distance) || distance <= 0f || distance > MaxDistance)
        {
            point = default;
            return false;
        }

        point = ray.GetPoint(distance);
        return true;
    }
}
