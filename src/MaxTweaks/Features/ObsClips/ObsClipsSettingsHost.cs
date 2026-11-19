using System;
using System.ComponentModel;
using BeatSaberMarkupLanguage.Attributes;
using MaxTweaks.Configuration;

namespace MaxTweaks.Features.ObsClips;

/// <summary>
/// BSML host for this feature's section on the MaxTweaks settings page (UI/Settings.bsml). The
/// status line follows the connection live: BSML re-reads a value when the host raises
/// PropertyChanged for it.
/// </summary>
internal class ObsClipsSettingsHost : INotifyPropertyChanged, IDisposable
{
    private readonly ObsClipsConfig _config;
    private readonly ObsClipsService _service;

    public ObsClipsSettingsHost(ObsClipsConfig config, ObsClipsService service)
    {
        _config = config;
        _service = service;
        _service.StatusChanged += HandleStatusChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    [UIValue("enabled")]
    public bool Enabled
    {
        get => _config.Enabled;
        set => _config.Enabled = value;
    }

    [UIValue("show-toasts")]
    public bool ShowToasts
    {
        get => _config.ShowToasts;
        set => _config.ShowToasts = value;
    }

    [UIValue("use-heart-rate")]
    public bool UseHeartRate
    {
        get => _config.UseHeartRate;
        set => _config.UseHeartRate = value;
    }

    [UIValue("clips-status")]
    public string ClipsStatus => "OBS: " + _service.StatusText;

    public void Dispose()
    {
        _service.StatusChanged -= HandleStatusChanged;
    }

    private void HandleStatusChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ClipsStatus)));
    }
}
