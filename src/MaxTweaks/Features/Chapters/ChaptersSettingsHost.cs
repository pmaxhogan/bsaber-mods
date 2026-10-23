using BeatSaberMarkupLanguage.Attributes;
using MaxTweaks.Configuration;

namespace MaxTweaks.Features.Chapters;

/// <summary>BSML host for this feature's tab on the MaxTweaks settings page (UI/Settings.bsml).</summary>
internal class ChaptersSettingsHost
{
    private readonly ChaptersConfig _config;

    public ChaptersSettingsHost(ChaptersConfig config)
    {
        _config = config;
    }

    [UIValue("enabled")]
    public bool Enabled
    {
        get => _config.Enabled;
        set => _config.Enabled = value;
    }

    [UIValue("show-progress-bars")]
    public bool ShowProgressBars
    {
        get => _config.ShowProgressBars;
        set => _config.ShowProgressBars = value;
    }
}
