namespace MaxTweaks.Configuration;

internal class AutoReloadConfig
{
    public virtual bool Enabled { get; set; } = true;

    /// <summary>Quiet period after the last file event before a reload runs.</summary>
    public virtual float DebounceSeconds { get; set; } = 2f;

    public virtual bool ShowToasts { get; set; } = true;
}
