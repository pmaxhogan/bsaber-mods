using UnityEngine;

namespace MaxTweaks.Common.Pointing;

/// <summary>
/// Lets one feature's world-space buttons take priority over another feature's lower-priority ray
/// targets without the features knowing each other: Chapters claims the pointer on every frame an
/// arrow is ray-hovered, and Active pause then does not pick notes behind it. A claim counts for the
/// frame it was made and the next one, since the order of the features' ticks is not fixed.
/// </summary>
internal static class PointerClaims
{
    private static int claimedFrame = int.MinValue;

    public static bool ClaimedRecently => Time.frameCount <= claimedFrame + 1;

    public static void Claim() => claimedFrame = Time.frameCount;
}
