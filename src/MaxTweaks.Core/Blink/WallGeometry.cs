using System;

namespace MaxTweaks.Core.Blink;

/// <summary>A wall's cross-section in the player's local frame, in metres (x = right, y = up).</summary>
public readonly struct WallBox
{
    public WallBox(float centerX, float width, float bottom, float height)
    {
        CenterX = centerX;
        Width = width;
        Bottom = bottom;
        Height = height;
    }

    public float CenterX { get; }

    public float Width { get; }

    public float Bottom { get; }

    public float Height { get; }

    public float Left => CenterX - (Width / 2f);

    public float Right => CenterX + (Width / 2f);

    public float Top => Bottom + Height;
}

/// <summary>Where the player's head may be while a wall passes, in the same frame as <see cref="WallBox"/>.</summary>
public readonly struct HeadZone
{
    public HeadZone(float xMin, float xMax, float yMin, float yMax)
    {
        XMin = Math.Min(xMin, xMax);
        XMax = Math.Max(xMin, xMax);
        YMin = Math.Min(yMin, yMax);
        YMax = Math.Max(yMin, yMax);
    }

    public float XMin { get; }

    public float XMax { get; }

    public float YMin { get; }

    public float YMax { get; }

    /// <summary>
    /// The head zone for a head at (x, y) that may also stand up to <paramref name="standingY"/>
    /// (so a player crouching right now is still protected from a wall at standing height), grown
    /// by <paramref name="margin"/> on every side (head size plus a little safety).
    /// </summary>
    public static HeadZone Around(float headX, float headY, float standingY, float margin)
    {
        return new HeadZone(
            headX - margin,
            headX + margin,
            Math.Min(headY, standingY) - margin,
            Math.Max(headY, standingY) + margin);
    }
}

/// <summary>
/// Wall placement maths, mirrored from the game (Beat Saber 1.40.8, Main.dll
/// BeatmapObjectSpawnMovementData.GetObstacleSpawnData / GetObstacleOffset and BeatmapCore.dll
/// StaticBeatmapObjectSpawnMovementData), so it can run without the spawn controller being
/// initialised and be unit tested.
/// </summary>
public static class WallGeometry
{
    public const float LaneWidth = 0.6f;
    public const float LayerHeight = 0.6f;
    public const float ObstacleVerticalOffset = -0.15f;
    public const float MinObstacleY = 0.1f;
    public const float ObstacleTopY = 3.1f;

    /// <summary>Line layers in a vanilla map: 0 = Base, 1 = Upper, 2 = Top.</summary>
    public const int LayerCount = 3;

    /// <summary>Widths/heights at or above this are Mapping Extensions precision values.</summary>
    public const int MappingExtensionsThreshold = 1000;

    /// <summary>Note line layer Y (StaticBeatmapObjectSpawnMovementData.LineYPosForLineLayer).</summary>
    public static float LineY(int layer) => layer switch
    {
        0 => 0.25f,
        1 => 0.85f,
        _ => 1.45f,
    };

    /// <summary>
    /// Computes the wall's box the way the game does. Returns false when the values are outside the
    /// vanilla range (Mapping Extensions / modded maps), where the game's own maths is replaced and
    /// the box would be meaningless: callers must then assume the wall hits the head.
    /// </summary>
    public static bool TryGetVanillaBox(int lineIndex, int lineLayer, int width, int height, int lineCount, float jumpOffsetY, out WallBox box)
    {
        box = default;
        if (lineCount <= 0 || lineIndex < 0 || lineIndex >= lineCount
            || lineLayer < 0 || lineLayer >= LayerCount
            || width <= 0 || width >= MappingExtensionsThreshold
            || height <= 0 || height >= MappingExtensionsThreshold
            || float.IsNaN(jumpOffsetY) || float.IsInfinity(jumpOffsetY))
        {
            return false;
        }

        var laneX = ((-(lineCount - 1) * 0.5f) + lineIndex) * LaneWidth;
        var w = width * LaneWidth;
        var centerX = laneX + ((w - LaneWidth) * 0.5f);
        var bottom = Math.Max(LineY(lineLayer) + ObstacleVerticalOffset + jumpOffsetY, MinObstacleY);
        var h = Math.Min(height * LayerHeight, ObstacleTopY - bottom);
        box = new WallBox(centerX, w, bottom, h);
        return true;
    }

    /// <summary>True when the wall's cross-section overlaps the head zone (touching counts).</summary>
    public static bool Overlaps(WallBox box, HeadZone head)
    {
        if (box.Height <= 0f || box.Width <= 0f)
        {
            return false;
        }

        return box.Left <= head.XMax && box.Right >= head.XMin
            && box.Bottom <= head.YMax && box.Top >= head.YMin;
    }

    /// <summary>
    /// The full decision for one wall: modded (out-of-range) walls always count as hitting the head.
    /// </summary>
    public static bool HitsHead(int lineIndex, int lineLayer, int width, int height, int lineCount, float jumpOffsetY, HeadZone head)
    {
        return !TryGetVanillaBox(lineIndex, lineLayer, width, height, lineCount, jumpOffsetY, out var box) || Overlaps(box, head);
    }
}
