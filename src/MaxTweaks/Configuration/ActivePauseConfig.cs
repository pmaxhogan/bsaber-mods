namespace MaxTweaks.Configuration;

internal class ActivePauseConfig
{
    public virtual bool Enabled { get; set; } = true;

    /// <summary>Show beatmap objects mostly dissolved while active-paused (they cannot be hit).</summary>
    public virtual bool GhostNotes { get; set; } = true;
}
