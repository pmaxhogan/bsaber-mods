using BeatSaberMarkupLanguage.Attributes;
using MaxTweaks.Configuration;
using MaxTweaks.Core.DifficultySwitch;

namespace MaxTweaks.Features.DifficultySwitch;

/// <summary>BSML host for this feature's section on the MaxTweaks settings page (UI/Settings.bsml).</summary>
internal class DifficultySwitchSettingsHost
{
    private readonly DifficultySwitchConfig _config;

    public DifficultySwitchSettingsHost(DifficultySwitchConfig config)
    {
        _config = config;
    }

    [UIValue("enabled")]
    public bool Enabled
    {
        get => _config.Enabled;
        set => _config.Enabled = value;
    }

    [UIValue("clear-gap-seconds")]
    public float ClearGapSeconds
    {
        get => ClearGap.Clamp(_config.ClearGapSeconds);
        set => _config.ClearGapSeconds = ClearGap.Clamp(value);
    }

    [UIValue("slide-old-notes")]
    public bool SlideOldNotes
    {
        get => _config.SlideOldNotes;
        set => _config.SlideOldNotes = value;
    }

    [UIAction("format-seconds")]
    public string FormatSeconds(float value) => value.ToString("0.#") + " s";
}
