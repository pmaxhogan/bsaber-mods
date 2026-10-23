namespace MaxTweaks.Configuration;

internal class HideOstConfig
{
    /// <summary>Feature master switch (shows the toggle button at all).</summary>
    public virtual bool Enabled { get; set; } = true;

    /// <summary>Current state of the one-click toggle: true = official songs are hidden.</summary>
    public virtual bool Hidden { get; set; } = false;
}
