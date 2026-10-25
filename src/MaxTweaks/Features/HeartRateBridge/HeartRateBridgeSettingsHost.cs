using System;
using System.ComponentModel;
using BeatSaberMarkupLanguage.Attributes;
using MaxTweaks.Configuration;

namespace MaxTweaks.Features.HeartRateBridge;

/// <summary>
/// BSML host for this feature's section on the MaxTweaks settings page (UI/Settings.bsml). The
/// status line follows the bridge live: BSML re-reads a value when the host raises
/// PropertyChanged for it.
/// </summary>
internal class HeartRateBridgeSettingsHost : INotifyPropertyChanged, IDisposable
{
    private readonly HeartRateBridgeConfig _config;
    private readonly HeartRateBridgeService _service;

    public HeartRateBridgeSettingsHost(HeartRateBridgeConfig config, HeartRateBridgeService service)
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

    [UIValue("only-with-hrcounter")]
    public bool OnlyWithHRCounter
    {
        get => _config.OnlyWithHRCounter;
        set => _config.OnlyWithHRCounter = value;
    }

    [UIValue("show-toasts")]
    public bool ShowToasts
    {
        get => _config.ShowToasts;
        set => _config.ShowToasts = value;
    }

    [UIValue("bridge-status")]
    public string BridgeStatus => "Bridge: " + _service.StatusText;

    public void Dispose()
    {
        _service.StatusChanged -= HandleStatusChanged;
    }

    private void HandleStatusChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BridgeStatus)));
    }
}
