namespace MaxTweaks.Configuration;

internal class ObsRecordingConfig
{
    /// <summary>Off by default: it needs OBS running with its WebSocket server enabled.</summary>
    public virtual bool Enabled { get; set; } = false;

    /// <summary>Toast when a recording was saved and renamed, and when OBS cannot be used.</summary>
    public virtual bool ShowToasts { get; set; } = true;

    /// <summary>PC that runs OBS; this PC by default.</summary>
    public virtual string Host { get; set; } = "127.0.0.1";

    /// <summary>WebSocket server port; 0 = the port in OBS's own settings on this PC, else 4455.</summary>
    public virtual int Port { get; set; } = 0;

    /// <summary>
    /// WebSocket server password; empty = the one OBS has stored on this PC
    /// (%APPDATA%/obs-studio/plugin_config/obs-websocket/config.json). Never logged or shown.
    /// </summary>
    public virtual string Password { get; set; } = string.Empty;
}
