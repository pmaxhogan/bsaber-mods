using BeatSaberMarkupLanguage.Attributes;
using MaxTweaks.Configuration;

namespace MaxTweaks.Features.Blink;

/// <summary>BSML host for this feature's tab on the MaxTweaks settings page (UI/Settings.bsml).</summary>
internal class BlinkSettingsHost
{
    private readonly BlinkConfig _config;

    public BlinkSettingsHost(BlinkConfig config)
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
