using System.Collections.Generic;

namespace MaxTweaks.Core.ActivePause;

/// <summary>What an <see cref="ActivePauseStep"/> amount counts.</summary>
public enum StepUnit
{
    /// <summary>Song seconds: a plain offset, no quantization.</summary>
    Seconds,

    /// <summary>Beats of the map's tempo: quantized to the grid of the step size (see <see cref="StepMath"/>).</summary>
    Beats,
}

/// <summary>
/// One active-pause seek button: a label and a signed amount in seconds or beats. The twelve buttons,
/// left to right, are <see cref="All"/>.
/// </summary>
public sealed class ActivePauseStep
{
    private ActivePauseStep(string label, float amount, StepUnit unit)
    {
        Label = label;
        Amount = amount;
        Unit = unit;
    }

    /// <summary>
    /// The buttons in display order (backwards steps first, largest first):
    /// -1m -15s -5s -1s -1b -.25b +.25b +1b +1s +5s +15s +1m.
    /// </summary>
    public static IReadOnlyList<ActivePauseStep> All { get; } = new[]
    {
        new ActivePauseStep("-1m", -60f, StepUnit.Seconds),
        new ActivePauseStep("-15s", -15f, StepUnit.Seconds),
        new ActivePauseStep("-5s", -5f, StepUnit.Seconds),
        new ActivePauseStep("-1s", -1f, StepUnit.Seconds),
        new ActivePauseStep("-1b", -1f, StepUnit.Beats),
        new ActivePauseStep("-.25b", -0.25f, StepUnit.Beats),
        new ActivePauseStep("+.25b", 0.25f, StepUnit.Beats),
        new ActivePauseStep("+1b", 1f, StepUnit.Beats),
        new ActivePauseStep("+1s", 1f, StepUnit.Seconds),
        new ActivePauseStep("+5s", 5f, StepUnit.Seconds),
        new ActivePauseStep("+15s", 15f, StepUnit.Seconds),
        new ActivePauseStep("+1m", 60f, StepUnit.Seconds),
    };

    /// <summary>Button text, e.g. "+.25b".</summary>
    public string Label { get; }

    /// <summary>Signed step size in <see cref="Unit"/>; negative steps go backwards.</summary>
    public float Amount { get; }

    public StepUnit Unit { get; }

    /// <summary>+1 for forward steps, -1 for backward steps.</summary>
    public int Direction => Amount < 0f ? -1 : 1;

    /// <summary>Looks up a step by its label (case-insensitive), or null.</summary>
    public static ActivePauseStep? Find(string label)
    {
        foreach (var step in All)
        {
            if (string.Equals(step.Label, label, System.StringComparison.OrdinalIgnoreCase))
            {
                return step;
            }
        }

        return null;
    }

    public override string ToString() => Label;
}
