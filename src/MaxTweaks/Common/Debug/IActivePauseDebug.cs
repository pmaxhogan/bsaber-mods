namespace MaxTweaks.Common.Debugging;

/// <summary>
/// Debug-channel access to Active pause flags (FPFC QA without aiming): implemented by the active
/// pause controller, which registers itself in <see cref="Current"/> while its song runs. Each method
/// returns the "ok" message or throws DebugCommandException.
/// </summary>
internal interface IActivePauseDebug
{
    /// <summary>Toggles the selection of the n-th selectable live note (0-based, by time, then line).</summary>
    string SelectNote(int index);

    string FlagOpen();

    string FlagToggle(string labelId);

    string FlagSave();

    string FlagCancel();

    /// <summary>Deselects every note.</summary>
    string FlagsClear();

    /// <summary>Presses the step button with this label (e.g. "+1s") count times in this frame.</summary>
    string Step(string label, int count);

    string Describe();
}

/// <summary>The registered <see cref="IActivePauseDebug"/>, if any.</summary>
internal static class ActivePauseDebugHook
{
    public static IActivePauseDebug? Current { get; set; }
}
