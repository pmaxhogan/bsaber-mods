using BeatSaberMarkupLanguage.Attributes;
using MaxTweaks.Configuration;

namespace MaxTweaks.Features.ObsStatus;

/// <summary>BSML host for this feature's section on the MaxTweaks settings page (UI/Settings.bsml).</summary>
internal class ObsStatusSettingsHost
{
    private readonly ObsStatusConfig _config;

    public ObsStatusSettingsHost(ObsStatusConfig config)
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
