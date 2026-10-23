using System;

namespace MaxTweaks.Core.DifficultySwitch;

/// <summary>What one <see cref="SwitchInputMachine.Update"/> call did.</summary>
public enum SwitchInputEvent
{
    /// <summary>Nothing changed.</summary>
    None,

    /// <summary>The list opened (Y pressed); the highlight starts on the current difficulty.</summary>
    Opened,

    /// <summary>The highlight moved one row (see <see cref="SwitchInputMachine.LastStep"/>).</summary>
    Moved,

    /// <summary>Y released on another difficulty: switch to <see cref="SwitchInputMachine.CommittedIndex"/>.</summary>
    Committed,

    /// <summary>The list closed without a switch (released on the current difficulty, or input stopped being allowed).</summary>
    Cancelled,
}

/// <summary>
/// Hold-Y difficulty picker input, independent of the game. Call <see cref="Update"/> once per frame.
///
/// Rules:
/// - Opens on a Y press edge (Y held now, not held on the previous update) while input is allowed.
///   Holding Y from before input became allowed does not open it; release and press again.
/// - While open, the right stick moves the highlight one row per flick: forward (y &gt; 0) = up = a
///   harder difficulty (lower index), back = down. A flick counts when |y| reaches
///   <see cref="PressThreshold"/>; the next flick needs the stick back inside
///   <see cref="RearmThreshold"/> first (hysteresis, no auto-repeat while held). A stick already
///   pushed when the list opens must be re-centred before it counts. The highlight stops at the ends
///   (no wrap-around).
/// - Releasing Y commits the highlighted row, or cancels when it is the current difficulty.
/// - When input stops being allowed while open (song ended, a switch is in progress) the list
///   closes with <see cref="SwitchInputEvent.Cancelled"/>. Pausing the game does not matter: the
///   caller keeps input allowed while paused.
/// </summary>
public sealed class SwitchInputMachine
{
    /// <summary>|stick y| at or above this is a flick.</summary>
    public const float PressThreshold = 0.6f;

    /// <summary>|stick y| at or below this re-arms the next flick.</summary>
    public const float RearmThreshold = 0.3f;

    private bool _yWasHeld = true; // a Y already held at the first update must not open the list
    private bool _armed;

    public bool IsOpen { get; private set; }

    /// <summary>Highlighted row while open (index into the hardest-first list).</summary>
    public int Highlight { get; private set; }

    /// <summary>Row of the difficulty being played, as given to the last update.</summary>
    public int CurrentIndex { get; private set; }

    /// <summary>Row chosen by the last <see cref="SwitchInputEvent.Committed"/>.</summary>
    public int CommittedIndex { get; private set; } = -1;

    /// <summary>Direction of the last <see cref="SwitchInputEvent.Moved"/>: -1 = up (harder), +1 = down.</summary>
    public int LastStep { get; private set; }

    /// <param name="yHeld">Y (or its keyboard fallback) is down this frame.</param>
    /// <param name="stickY">Right stick forward/back in [-1, 1]; NaN counts as centred.</param>
    /// <param name="allowed">Practice gameplay is running or paused and no switch is in progress.</param>
    /// <param name="currentIndex">Row of the difficulty being played.</param>
    /// <param name="count">Number of rows (difficulties of the current characteristic).</param>
    public SwitchInputEvent Update(bool yHeld, float stickY, bool allowed, int currentIndex, int count)
    {
        if (float.IsNaN(stickY))
        {
            stickY = 0f;
        }

        var wasHeld = _yWasHeld;
        _yWasHeld = yHeld;
        CurrentIndex = currentIndex;

        if (!allowed || count <= 0 || currentIndex < 0 || currentIndex >= count)
        {
            return Close(SwitchInputEvent.Cancelled);
        }

        if (!IsOpen)
        {
            if (!yHeld || wasHeld)
            {
                return SwitchInputEvent.None;
            }

            IsOpen = true;
            Highlight = currentIndex;
            _armed = Math.Abs(stickY) <= RearmThreshold;
            return SwitchInputEvent.Opened;
        }

        if (Highlight >= count)
        {
            Highlight = count - 1;
        }

        if (!yHeld)
        {
            if (Highlight != currentIndex)
            {
                CommittedIndex = Highlight;
                IsOpen = false;
                return SwitchInputEvent.Committed;
            }

            return Close(SwitchInputEvent.Cancelled);
        }

        var magnitude = Math.Abs(stickY);
        if (_armed && magnitude >= PressThreshold)
        {
            _armed = false;
            var step = stickY > 0f ? -1 : 1;
            var next = Highlight + step;
            if (next >= 0 && next < count)
            {
                Highlight = next;
                LastStep = step;
                return SwitchInputEvent.Moved;
            }
        }
        else if (!_armed && magnitude <= RearmThreshold)
        {
            _armed = true;
        }

        return SwitchInputEvent.None;
    }

    /// <summary>Closes the list without a switch (no event when it was not open).</summary>
    public void Reset() => IsOpen = false;

    private SwitchInputEvent Close(SwitchInputEvent ifOpen)
    {
        if (!IsOpen)
        {
            return SwitchInputEvent.None;
        }

        IsOpen = false;
        return ifOpen;
    }
}
