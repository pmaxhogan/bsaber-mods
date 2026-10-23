using BeatSaberMarkupLanguage.Attributes;
using MaxTweaks.Configuration;

namespace MaxTweaks.Features.AutoReload;

/// <summary>BSML host for this feature's tab on the MaxTweaks settings page (UI/Settings.bsml).</summary>
internal class AutoReloadSettingsHost
{
    private readonly AutoReloadConfig _config;

    public AutoReloadSettingsHost(AutoReloadConfig config)
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
