using IPA.Config.Stores.Attributes;

namespace MaxTweaks.Configuration;

/// <summary>
/// Root BSIPA config (UserData/MaxTweaks.json). One nested section per feature; each section's
/// class lives in its own file so features can evolve independently. Properties must be virtual
/// and classes must not be sealed (BSIPA generates a subclass that auto-saves on set).
/// </summary>
internal class PluginConfig
{
    [NonNullable]
    public virtual HideOstConfig HideOst { get; set; } = new();

    [NonNullable]
    public virtual AutoReloadConfig AutoReload { get; set; } = new();

    [NonNullable]
    public virtual BlinkConfig Blink { get; set; } = new();

    [NonNullable]
    public virtual ChaptersConfig Chapters { get; set; } = new();

    [NonNullable]
    public virtual DifficultySwitchConfig DifficultySwitch { get; set; } = new();

    [NonNullable]
    public virtual ActivePauseConfig ActivePause { get; set; } = new();

    [NonNullable]
    public virtual HeartRateBridgeConfig HeartRateBridge { get; set; } = new();
}
