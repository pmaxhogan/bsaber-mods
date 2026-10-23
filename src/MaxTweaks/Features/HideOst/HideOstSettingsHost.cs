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
}
