namespace MaxTweaks.Common.Debugging;

/// <summary>
/// Debug-channel access to OBS clips (FPFC QA, where no notes are hit and the picker never fires):
/// implemented by the clips service, which registers itself in <see cref="ObsClipsDebugHook.Current"/>
/// for the lifetime of the app. Each method returns the "ok" message or throws DebugCommandException.
/// </summary>
internal interface IObsClipsDebug
{
    /// <summary>Saves the replay buffer and cuts the stretch that started secondsAgo and is seconds long, like a picked clip.</summary>
    string ClipNow(double secondsAgo, double seconds);

    /// <summary>
    /// On: a missed note counts as a hit, so the picker has something to pick in FPFC. Off again
    /// when the game restarts. Returns the new state as text.
    /// </summary>
    string CountMissesAsHits(bool on);

    /// <summary>Status fragment: "clips=ready buffer=60s budget=1.25".</summary>
    string Describe();
}

/// <summary>The registered <see cref="IObsClipsDebug"/>, if any.</summary>
internal static class ObsClipsDebugHook
{
    public static IObsClipsDebug? Current { get; set; }
}
