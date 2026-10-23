using System;
using Newtonsoft.Json.Linq;

namespace MaxTweaks.Core.Chapters;

/// <summary>The in-game clock of the Enhancements mod (UserData/Enhancements.json, "Clock" section).</summary>
public sealed class ClockPlacement
{
    public ClockPlacement(float y, float z, float pitchDegrees, float fontSize)
    {
        Y = y;
        Z = z;
        PitchDegrees = pitchDegrees;
        FontSize = fontSize;
    }

    /// <summary>World height of the clock screen centre (default 3.0).</summary>
    public float Y { get; }

    /// <summary>World distance ahead of the clock screen centre (default 3.9).</summary>
    public float Z { get; }

    /// <summary>Euler X rotation of the clock screen (default 325, i.e. tilted 35 degrees towards the player).</summary>
    public float PitchDegrees { get; }

    /// <summary>Clock font size in BSML screen units (default 10).</summary>
    public float FontSize { get; }

    /// <summary>Enhancements 3.0.18 defaults.</summary>
    public static ClockPlacement EnhancementsDefault { get; } = new(3.0f, 3.9f, 325f, 10f);
}

/// <summary>
/// Where the chapter stack goes. It sits 7 m ahead, centred over the track, and its bottom edge must
/// appear above the gameplay clock as seen from the player's eyes. The clock (Enhancements) is much
/// closer (3.9 m) and tilted, so the check is angular: the stack's bottom must be at a higher
/// elevation angle than the top of the clock text, plus a margin.
/// </summary>
public static class ChaptersPlacement
{
    /// <summary>Distance of the stack ahead of the player.</summary>
    public const float StackZ = 7f;

    /// <summary>Lowest bottom edge ever used: keeps clear of HRCounter's heart (about 2.5-2.7 m at z 7).</summary>
    public const float MinBottomY = 4.0f;

    /// <summary>Gap between the clock's top and the stack's bottom, in degrees of view.</summary>
    public const float MarginDegrees = 1.5f;

    /// <summary>BSML floating screens are scaled 0.02 (1 unit = 2 cm).</summary>
    public const float ScreenScale = 0.02f;

    /// <summary>Line height of the game's Teko font relative to its size (measured in game: 15.8 for 11).</summary>
    public const float LineHeightRatio = 1.45f;

    /// <summary>World Y of the stack's bottom edge.</summary>
    /// <param name="clock">The clock, or null if none is shown in gameplay.</param>
    /// <param name="eyeHeight">Eye height to plan for; lower eyes see the clock higher, so pass a conservative (low) value.</param>
    public static float BottomY(ClockPlacement? clock, float eyeHeight)
    {
        if (clock == null || !(clock.Z > 0.1f) || float.IsNaN(eyeHeight))
        {
            return MinBottomY;
        }

        var pitch = clock.PitchDegrees * Math.PI / 180.0;
        var halfText = clock.FontSize * LineHeightRatio * ScreenScale * 0.5;

        // Local up of a screen rotated by Euler X = pitch (Unity, left-handed): (0, cos, sin).
        var topY = clock.Y + (Math.Cos(pitch) * halfText);
        var topZ = clock.Z + (Math.Sin(pitch) * halfText);
        if (topZ <= 0.1)
        {
            return MinBottomY;
        }

        var elevation = Math.Atan2(topY - eyeHeight, topZ) + (MarginDegrees * Math.PI / 180.0);
        if (elevation >= 80 * Math.PI / 180.0)
        {
            return MinBottomY;
        }

        var y = eyeHeight + (StackZ * Math.Tan(elevation));
        return (float)Math.Max(MinBottomY, y);
    }

    /// <summary>
    /// Reads the clock from Enhancements.json text. Returns null when the clock is disabled or hidden in
    /// game; returns the defaults for any value that is missing or unreadable.
    /// </summary>
    public static ClockPlacement? ParseEnhancementsClock(string? json)
    {
        var defaults = ClockPlacement.EnhancementsDefault;
        if (Bookmarks.BookmarkParser.TryRead(json) is not JObject root || ChaptersParser.GetProperty(root, "Clock") is not JObject clock)
        {
            return defaults;
        }

        if (!Flag(clock, "Enabled") || !Flag(clock, "ShowInGame"))
        {
            return null;
        }

        var position = ChaptersParser.GetProperty(clock, "Position") as JObject;
        var rotation = ChaptersParser.GetProperty(clock, "Rotation") as JObject;
        return new ClockPlacement(
            Number(position, "y", defaults.Y),
            Number(position, "z", defaults.Z),
            Number(rotation, "x", defaults.PitchDegrees),
            Number(clock, "Size", defaults.FontSize));
    }

    private static bool Flag(JObject obj, string name)
    {
        var token = ChaptersParser.GetProperty(obj, name);
        return token == null || token.Type != JTokenType.Boolean || (bool)token;
    }

    private static float Number(JObject? obj, string name, float fallback)
    {
        var token = obj == null ? null : ChaptersParser.GetProperty(obj, name);
        if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float))
        {
            return fallback;
        }

        var value = ChaptersParser.ToDouble(token);
        return value is > -1000 and < 1000 ? (float)value : fallback;
    }
}
