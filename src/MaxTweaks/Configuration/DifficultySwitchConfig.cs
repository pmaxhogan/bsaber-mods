namespace MaxTweaks.Configuration;

internal class DifficultySwitchConfig
{
    public virtual bool Enabled { get; set; } = true;

    /// <summary>Seconds after a switch with every beatmap object removed.</summary>
    public virtual float ClearGapSeconds { get; set; } = 3f;

    /// <summary>Animate notes already in flight sliding out of the way on a switch.</summary>
    public virtual bool SlideOldNotes { get; set; } = true;
}
