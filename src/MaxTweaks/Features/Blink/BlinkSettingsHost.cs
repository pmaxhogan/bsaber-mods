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

    [UIValue("quiet-before")]
    public float QuietBefore
    {
        get => _config.QuietBeforeSeconds;
        set => _config.QuietBeforeSeconds = value;
    }

    [UIValue("clear-ahead")]
    public float ClearAhead
    {
        get => _config.ClearAheadSeconds;
        set => _config.ClearAheadSeconds = value;
    }

    [UIValue("duration")]
    public float Duration
    {
        get => _config.DurationSeconds;
        set => _config.DurationSeconds = value;
    }

    [UIValue("cooldown")]
    public float Cooldown
    {
        get => _config.CooldownSeconds;
        set => _config.CooldownSeconds = value;
    }

    [UIValue("vignette-strength")]
    public float VignetteStrength
    {
        get => _config.VignetteStrength;
        set => _config.VignetteStrength = value;
    }

    [UIAction("format-seconds")]
    public string FormatSeconds(float value) => value.ToString("0.#") + " s";

    [UIAction("format-cooldown")]
    public string FormatCooldown(float value)
    {
        var seconds = (int)value;
        return seconds % 60 == 0 ? (seconds / 60) + " min" : (seconds / 60) + " min " + (seconds % 60) + " s";
    }

    [UIAction("format-percent")]
    public string FormatPercent(float value) => ((int)System.Math.Round(value * 100f)) + "%";
}
