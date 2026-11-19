namespace MaxTweaks.Configuration;

/// <summary>
/// OBS clips. Where OBS is (host, port, password) is not repeated here: it is the same OBS as for
/// OBS recording, so the ObsRecording section's Host, Port and Password apply.
/// </summary>
internal class ObsClipsConfig
{
    /// <summary>Off by default: it needs OBS running with its WebSocket server and replay buffer enabled.</summary>
    public virtual bool Enabled { get; set; } = false;

    /// <summary>Toast after a song with the number of clips it gave, and when OBS cannot be used.</summary>
    public virtual bool ShowToasts { get; set; } = true;

    /// <summary>One clip per this many seconds of unpaused play, on average.</summary>
    public virtual int SecondsPerClip { get; set; } = 120;

    /// <summary>Length of a clip in seconds (3 to 30). The cut adds up to about 3 s in front.</summary>
    public virtual int ClipSeconds { get; set; } = 10;

    /// <summary>Also clip when the heart rate climbs quickly (needs the heart rate bridge).</summary>
    public virtual bool UseHeartRate { get; set; } = true;

    /// <summary>Keep the whole replay OBS saved next to the clip cut out of it.</summary>
    public virtual bool KeepFullReplay { get; set; } = false;

    /// <summary>ffmpeg executable; empty = "ffmpeg" from PATH.</summary>
    public virtual string FfmpegPath { get; set; } = string.Empty;
}
