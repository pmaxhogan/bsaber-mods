using BeatSaberMarkupLanguage.Attributes;
using MaxTweaks.Configuration;

namespace MaxTweaks.Features.ActivePause;

/// <summary>BSML host for this feature's section on the MaxTweaks settings page (UI/Settings.bsml).</summary>
internal class ActivePauseSettingsHost
{
    private readonly ActivePauseConfig _config;

    public ActivePauseSettingsHost(ActivePauseConfig config)
    {
        _config = config;
    }

    [UIValue("enabled")]
    public bool Enabled
    {
        get => _config.Enabled;
        set => _config.Enabled = value;
    }

    [UIValue("ghost-notes")]
    public bool GhostNotes
    {
        get => _config.GhostNotes;
        set => _config.GhostNotes = value;
    }
}
