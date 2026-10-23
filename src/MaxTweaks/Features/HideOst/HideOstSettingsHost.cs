using BeatSaberMarkupLanguage.Attributes;
using MaxTweaks.Configuration;

namespace MaxTweaks.Features.HideOst;

/// <summary>BSML host for this feature's tab on the MaxTweaks settings page (UI/Settings.bsml).</summary>
internal class HideOstSettingsHost
{
    private readonly HideOstConfig _config;
    private readonly HideOstController _controller;

    public HideOstSettingsHost(HideOstConfig config, HideOstController controller)
    {
        _config = config;
        _controller = controller;
    }

    [UIValue("enabled")]
    public bool Enabled
    {
        get => _config.Enabled;
        set => _controller.SetEnabled(value);
    }

    /// <summary>Same state as the song-list button; applies live if the solo list is open.</summary>
    [UIValue("hidden")]
    public bool Hidden
    {
        get => _config.Hidden;
        set => _controller.SetHidden(value);
    }
}
