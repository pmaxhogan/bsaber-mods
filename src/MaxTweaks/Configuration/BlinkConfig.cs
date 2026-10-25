namespace MaxTweaks.Configuration;

internal class BlinkConfig
{
    public virtual bool Enabled { get; set; } = true;

    /// <summary>No notes for at least this long before the blink can start.</summary>
    public virtual float QuietBeforeSeconds { get; set; } = 3f;

    /// <summary>No notes and no head-height walls for at least this long ahead.</summary>
    public virtual float ClearAheadSeconds { get; set; } = 8f;

    /// <summary>Total blink length including fades.</summary>
    public virtual float DurationSeconds { get; set; } = 3f;

    /// <summary>Minimum played time between blinks; carries across songs.</summary>
    public virtual float CooldownSeconds { get; set; } = 300f;

    /// <summary>Vignette darkness at the corners, 0..1.</summary>
    public virtual float VignetteStrength { get; set; } = 0.6f;

    /// <summary>
    /// Draw the overlay as a Screen Space - Camera canvas (Unity's VR UI mode) instead of a
    /// head-locked world quad. Try it if the vignette shows in one eye only.
    /// </summary>
    public virtual bool ScreenSpaceOverlay { get; set; } = false;
}
