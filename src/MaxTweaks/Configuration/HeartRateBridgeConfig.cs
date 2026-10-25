namespace MaxTweaks.Configuration;

internal class HeartRateBridgeConfig
{
    /// <summary>Off by default: it needs a Bluetooth heart-rate strap, HRCounter and uv on this PC.</summary>
    public virtual bool Enabled { get; set; } = false;

    /// <summary>Only start when the HRCounter mod is loaded (the bridge is useless without it).</summary>
    public virtual bool OnlyWithHRCounter { get; set; } = true;

    /// <summary>Toast when the strap connects or drops.</summary>
    public virtual bool ShowToasts { get; set; } = true;

    /// <summary>Program that runs the script; "uv" from PATH by default.</summary>
    public virtual string UvPath { get; set; } = "uv";

    /// <summary>Bridge script; empty = UserData/MaxTweaks/hr_osc_bridge.py (shipped in the release zip).</summary>
    public virtual string ScriptPath { get; set; } = string.Empty;

    /// <summary>Extra script arguments, e.g. "--name TICKR" or "--port 9001".</summary>
    public virtual string ExtraArguments { get; set; } = string.Empty;
}
