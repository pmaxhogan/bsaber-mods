using BeatSaberMarkupLanguage.Attributes;
using MaxTweaks.Configuration;

namespace MaxTweaks.Features.HideOst;

/// <summary>BSML host for this feature's tab on the MaxTweaks settings page (UI/Settings.bsml).</summary>
internal class HideOstSettingsHost
{
    private readonly HideOstConfig _config;

    public HideOstSettingsHost(HideOstConfig config)
    {
        _config = config;
    }

    [UIValue("enabled")]
    public bool Enabled
    {
        get => _config.Enabled;
        set => _config.Enabled = value;
    }

    /// <summary>Same state as the song-list button; applies the next time the song list opens.</summary>
    [UIValue("hidden")]
    public bool Hidden
    {
        get => _config.Hidden;
        set => _config.Hidden = value;
    }
}
