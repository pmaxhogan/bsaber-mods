using System;

namespace MaxTweaks.Core.DifficultySwitch;

/// <summary>What one <see cref="SwitchInputMachine.Update(bool, float, float, bool, int, int, int, int)"/> call did.</summary>
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

    /// <summary>
    /// The first left/right flick opened the NJS row; the row highlight moved one value from the
    /// current one when it could (see <see cref="SwitchInputMachine.LastRowStep"/>, 0 at an end).
    /// </summary>
    RowOpened,

    /// <summary>The NJS row highlight moved one value (see <see cref="SwitchInputMachine.LastRowStep"/>).</summary>
    RowMoved,

    /// <summary>Y released with the list on the current difficulty and the row on another value: <see cref="SwitchInputMachine.CommittedRowIndex"/>.</summary>
    RowCommitted,

    /// <summary>A left/right flick while there is no NJS row (row count 0).</summary>
    RowUnavailable,
}

/// <summary>
/// Hold-Y difficulty picker input, independent of the game. Call
/// <see cref="Update(bool, float, float, bool, int, int, int, int)"/> once per frame.
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
/// - Left/right works the same way on the NJS row (right = next, higher value), with its own arming
///   so the two axes never block each other. The first left/right flick opens the row and moves one
///   value from the current one. When both axes pass the threshold in the same update only the
///   larger one counts and both must re-centre. With no row (row count 0) a left/right flick reports
///   <see cref="SwitchInputEvent.RowUnavailable"/> instead.
/// - Releasing Y commits the highlighted difficulty when it is not the current one (the difficulty
///   wins over the row), else the highlighted NJS when the row is open on another value, else cancels.
/// - When input stops being allowed while open (song ended, a switch is in progress) the list
///   closes with <see cref="SwitchInputEvent.Cancelled"/>. Pausing the game does not matter: the
///   caller keeps input allowed while paused.
/// </summary>
public sealed class SwitchInputMachine
{
    /// <summary>|stick| on an axis at or above this is a flick.</summary>
    public const float PressThreshold = 0.6f;

    /// <summary>|stick| on an axis at or below this re-arms the next flick on that axis.</summary>
    public const float RearmThreshold = 0.3f;

    private bool _yWasHeld = true; // a Y already held at the first update must not open the list
    private bool _armedY;
    private bool _armedX;

    public bool IsOpen { get; private set; }

    /// <summary>Highlighted row while open (index into the hardest-first list).</summary>
    public int Highlight { get; private set; }

    /// <summary>Row of the difficulty being played, as given to the last update.</summary>
    public int CurrentIndex { get; private set; }

    /// <summary>Row chosen by the last <see cref="SwitchInputEvent.Committed"/>.</summary>
    public int CommittedIndex { get; private set; } = -1;

    /// <summary>Direction of the last <see cref="SwitchInputEvent.Moved"/>: -1 = up (harder), +1 = down.</summary>
    public int LastStep { get; private set; }

    /// <summary>The NJS row is shown (a left/right flick since the list opened).</summary>
    public bool RowOpen { get; private set; }

    /// <summary>Highlighted NJS row index while <see cref="RowOpen"/>.</summary>
    public int RowHighlight { get; private set; }

    /// <summary>NJS row index chosen by the last <see cref="SwitchInputEvent.RowCommitted"/>.</summary>
    public int CommittedRowIndex { get; private set; } = -1;

    /// <summary>Direction of the last row move: -1 = left (lower), +1 = right (higher), 0 = none (at an end).</summary>
    public int LastRowStep { get; private set; }

    /// <summary>Vertical list only (no NJS row).</summary>
    /// <param name="yHeld">Y (or its keyboard fallback) is down this frame.</param>
    /// <param name="stickY">Right stick forward/back in [-1, 1]; NaN counts as centred.</param>
    /// <param name="allowed">Practice gameplay is running or paused and no switch is in progress.</param>
    /// <param name="currentIndex">Row of the difficulty being played.</param>
    /// <param name="count">Number of rows (difficulties of the current characteristic).</param>
    public SwitchInputEvent Update(bool yHeld, float stickY, bool allowed, int currentIndex, int count) =>
        Update(yHeld, 0f, stickY, allowed, currentIndex, count, 0, 0);

    /// <param name="yHeld">Y (or its keyboard fallback) is down this frame.</param>
    /// <param name="stickX">Right stick left/right in [-1, 1]; NaN counts as centred.</param>
    /// <param name="stickY">Right stick forward/back in [-1, 1]; NaN counts as centred.</param>
    /// <param name="allowed">Practice gameplay is running or paused and no switch is in progress.</param>
    /// <param name="currentIndex">Row of the difficulty being played.</param>
    /// <param name="count">Number of rows (difficulties of the current characteristic).</param>
    /// <param name="rowCount">Number of NJS values; 0 = no NJS row on this song.</param>
    /// <param name="rowCurrent">Index of the NJS in effect now (out of range = no row).</param>
    public SwitchInputEvent Update(bool yHeld, float stickX, float stickY, bool allowed, int currentIndex, int count, int rowCount, int rowCurrent)
    {
        if (float.IsNaN(stickY))
        {
            stickY = 0f;
        }

        if (float.IsNaN(stickX))
        {
            stickX = 0f;
        }

        if (rowCount <= 0 || rowCurrent < 0 || rowCurrent >= rowCount)
        {
            rowCount = 0;
            RowOpen = false;
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
            RowOpen = false;
            Highlight = currentIndex;
            _armedY = Math.Abs(stickY) <= RearmThreshold;
            _armedX = Math.Abs(stickX) <= RearmThreshold;
            return SwitchInputEvent.Opened;
        }

        if (Highlight >= count)
        {
            Highlight = count - 1;
        }

        if (RowOpen && RowHighlight >= rowCount)
        {
            RowHighlight = rowCount - 1;
        }

        if (!yHeld)
        {
            IsOpen = false;
            var rowOpen = RowOpen;
            RowOpen = false;
            if (Highlight != currentIndex)
            {
                CommittedIndex = Highlight;
                return SwitchInputEvent.Committed;
            }

            if (rowOpen && RowHighlight != rowCurrent)
            {
                CommittedRowIndex = RowHighlight;
                return SwitchInputEvent.RowCommitted;
            }

            return SwitchInputEvent.Cancelled;
        }

        var absY = Math.Abs(stickY);
        var absX = Math.Abs(stickX);
        var flickY = _armedY && absY >= PressThreshold;
        var flickX = _armedX && absX >= PressThreshold;
        if (flickY || flickX)
        {
            // Whichever axis is past the threshold must re-centre, the counted one and the other.
            if (absY >= PressThreshold)
            {
                _armedY = false;
            }

            if (absX >= PressThreshold)
            {
                _armedX = false;
            }

            var useRow = flickX && (!flickY || absX > absY);
            return useRow ? StepRow(stickX, rowCount, rowCurrent) : StepList(stickY, count);
        }

        if (!_armedY && absY <= RearmThreshold)
        {
            _armedY = true;
        }

        if (!_armedX && absX <= RearmThreshold)
        {
            _armedX = true;
        }

        return SwitchInputEvent.None;
    }

    /// <summary>Closes the list without a switch (no event when it was not open).</summary>
    public void Reset()
    {
        IsOpen = false;
        RowOpen = false;
    }

    private SwitchInputEvent StepList(float stickY, int count)
    {
        var step = stickY > 0f ? -1 : 1;
        var next = Highlight + step;
        if (next < 0 || next >= count)
        {
            return SwitchInputEvent.None;
        }

        Highlight = next;
        LastStep = step;
        return SwitchInputEvent.Moved;
    }

    private SwitchInputEvent StepRow(float stickX, int rowCount, int rowCurrent)
    {
        if (rowCount <= 0)
        {
            return SwitchInputEvent.RowUnavailable;
        }

        var opening = !RowOpen;
        if (opening)
        {
            RowOpen = true;
            RowHighlight = rowCurrent;
        }

        var step = stickX > 0f ? 1 : -1;
        var next = RowHighlight + step;
        if (next >= 0 && next < rowCount)
        {
            RowHighlight = next;
            LastRowStep = step;
        }
        else
        {
            LastRowStep = 0;
            if (!opening)
            {
                return SwitchInputEvent.None;
            }
        }

        return opening ? SwitchInputEvent.RowOpened : SwitchInputEvent.RowMoved;
    }

    private SwitchInputEvent Close(SwitchInputEvent ifOpen)
    {
        RowOpen = false;
        if (!IsOpen)
        {
            return SwitchInputEvent.None;
        }

        IsOpen = false;
        return ifOpen;
    }
}
