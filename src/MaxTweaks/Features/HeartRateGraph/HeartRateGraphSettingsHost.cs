using BeatSaberMarkupLanguage.Attributes;
using MaxTweaks.Configuration;

namespace MaxTweaks.Features.HeartRateGraph;

/// <summary>BSML host for this feature's section on the MaxTweaks settings page (UI/Settings.bsml).</summary>
internal class HeartRateGraphSettingsHost
{
    private readonly HeartRateGraphConfig _config;

    public HeartRateGraphSettingsHost(HeartRateGraphConfig config)
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
