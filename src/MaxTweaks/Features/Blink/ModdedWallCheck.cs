using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace MaxTweaks.Features.Blink;

/// <summary>
/// Detects walls whose position the vanilla maths does not describe: Noodle Extensions custom
/// coordinates/size/rotation/animation, read from CustomJSONData's customData dictionary by
/// reflection (no dependency). Such walls are treated as head-hitting (conservative).
/// Runs once per wall when the song loads, never per frame.
/// </summary>
internal static class ModdedWallCheck
{
    // v2 (underscore) and v3 Noodle keys that move, resize, rotate or animate a wall.
    private static readonly string[] PlacementKeys =
    {
        "_position", "_scale", "_rotation", "_localRotation", "_animation", "_track",
        "coordinates", "size", "worldRotation", "localRotation", "animation", "track",
    };

    private static readonly Dictionary<Type, PropertyInfo?> CustomDataProperties = new();

    public static bool HasCustomPlacement(ObstacleData obstacle)
    {
        var type = obstacle.GetType();
        if (type == typeof(ObstacleData))
        {
            return false;
        }

        if (!CustomDataProperties.TryGetValue(type, out var property))
        {
            property = type.GetProperty("customData", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            CustomDataProperties[type] = property;
        }

        if (property?.GetValue(obstacle) is not IDictionary data)
        {
            return false;
        }

        foreach (var key in PlacementKeys)
        {
            if (data.Contains(key))
            {
                return true;
            }
        }

        return false;
    }
}
