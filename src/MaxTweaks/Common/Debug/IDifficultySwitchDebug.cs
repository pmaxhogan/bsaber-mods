namespace MaxTweaks.Common.Debugging;

/// <summary>
/// Debug-channel access to the difficulty switch's NJS row (FPFC QA): implemented by the difficulty
/// switch controller, which registers itself in <see cref="DifficultySwitchDebugHook.Current"/> while
/// its song runs. Each method returns the "ok" message or throws DebugCommandException.
/// </summary>
internal interface IDifficultySwitchDebug
{
    /// <summary>Applies <paramref name="njs"/> exactly like releasing Y on that NJS row value (null = the difficulty's own NJS).</summary>
    string SetNoteJumpSpeed(float? njs);

    /// <summary>Status fragment: "njs=18/16" (current/default) or "njs=unavailable".</summary>
    string Describe();
}

/// <summary>The registered <see cref="IDifficultySwitchDebug"/>, if any.</summary>
internal static class DifficultySwitchDebugHook
{
    public static IDifficultySwitchDebug? Current { get; set; }
}
