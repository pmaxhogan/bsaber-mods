using System.Collections.Generic;
using UnityEngine;

namespace MaxTweaks.Common.Pointing;

/// <summary>
/// Coordinates world-space pointing between features without them knowing each other.
///
/// Claims: Chapters claims the pointer on every frame an arrow is ray-hovered, and Active pause then
/// neither picks notes behind it nor uses A (triggers only). A claim counts for the frame it was made
/// and the next one, since the order of the features' ticks is not fixed.
///
/// Presses: one button edge does one thing. A feature acting on a press calls
/// <see cref="TryConsume"/> first; the second feature to ask in the same frame gets false, whatever
/// order the ticks run in.
/// </summary>
internal static class PointerClaims
{
    private static readonly Dictionary<ControllerButton, int> ConsumedFrame = new();
    private static int claimedFrame = int.MinValue;

    public static bool ClaimedRecently => Time.frameCount <= claimedFrame + 1;

    public static void Claim() => claimedFrame = Time.frameCount;

    /// <summary>True for the first caller this frame for <paramref name="button"/>; it now owns that press.</summary>
    public static bool TryConsume(ControllerButton button)
    {
        var frame = Time.frameCount;
        if (ConsumedFrame.TryGetValue(button, out var consumed) && consumed == frame)
        {
            return false;
        }

        ConsumedFrame[button] = frame;
        return true;
    }
}
