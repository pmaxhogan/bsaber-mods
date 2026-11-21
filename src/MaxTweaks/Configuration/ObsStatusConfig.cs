namespace MaxTweaks.Configuration;

internal class ObsStatusConfig
{
    /// <summary>
    /// On by default: nothing is drawn unless OBS recording or OBS clips is on (both are off by default).
    /// </summary>
    public virtual bool Enabled { get; set; } = true;
}
