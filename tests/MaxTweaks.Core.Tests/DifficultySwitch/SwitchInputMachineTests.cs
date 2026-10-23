using MaxTweaks.Core.DifficultySwitch;
using Xunit;

namespace MaxTweaks.Core.Tests.DifficultySwitch;

public class SwitchInputMachineTests
{
    // Hardest-first list of 4 difficulties; the played one is row 2.
    private const int Count = 4;
    private const int Current = 2;

    private static SwitchInputMachine Opened()
    {
        var m = new SwitchInputMachine();
        Assert.Equal(SwitchInputEvent.None, m.Update(false, 0f, true, Current, Count));
        Assert.Equal(SwitchInputEvent.Opened, m.Update(true, 0f, true, Current, Count));
        return m;
    }

    [Fact]
    public void OpensOnPressEdge_HighlightOnCurrent()
    {
        var m = Opened();
        Assert.True(m.IsOpen);
        Assert.Equal(Current, m.Highlight);
        Assert.Equal(SwitchInputEvent.None, m.Update(true, 0f, true, Current, Count));
    }

    [Fact]
    public void YHeldAtFirstUpdate_DoesNotOpen_UntilPressedAgain()
    {
        var m = new SwitchInputMachine();
        Assert.Equal(SwitchInputEvent.None, m.Update(true, 0f, true, Current, Count));
        Assert.Equal(SwitchInputEvent.None, m.Update(true, 0f, true, Current, Count));
        Assert.False(m.IsOpen);
        Assert.Equal(SwitchInputEvent.None, m.Update(false, 0f, true, Current, Count));
        Assert.Equal(SwitchInputEvent.Opened, m.Update(true, 0f, true, Current, Count));
    }

    [Fact]
    public void YPressedWhileNotAllowed_DoesNotOpenWhenAllowedLater()
    {
        var m = new SwitchInputMachine();
        m.Update(false, 0f, true, Current, Count);
        Assert.Equal(SwitchInputEvent.None, m.Update(true, 0f, false, Current, Count));
        Assert.Equal(SwitchInputEvent.None, m.Update(true, 0f, true, Current, Count));
        Assert.False(m.IsOpen);
    }

    [Fact]
    public void StickForward_MovesUpOneRow_PerFlick()
    {
        var m = Opened();
        Assert.Equal(SwitchInputEvent.Moved, m.Update(true, 1f, true, Current, Count));
        Assert.Equal(1, m.Highlight);
        Assert.Equal(-1, m.LastStep);

        // Still held: no auto-repeat.
        Assert.Equal(SwitchInputEvent.None, m.Update(true, 1f, true, Current, Count));
        Assert.Equal(SwitchInputEvent.None, m.Update(true, 0.5f, true, Current, Count));
        Assert.Equal(1, m.Highlight);

        // Back inside the re-arm zone, then another flick.
        Assert.Equal(SwitchInputEvent.None, m.Update(true, 0.2f, true, Current, Count));
        Assert.Equal(SwitchInputEvent.Moved, m.Update(true, 0.7f, true, Current, Count));
        Assert.Equal(0, m.Highlight);
    }

    [Fact]
    public void StickBack_MovesDown()
    {
        var m = Opened();
        Assert.Equal(SwitchInputEvent.Moved, m.Update(true, -0.9f, true, Current, Count));
        Assert.Equal(3, m.Highlight);
        Assert.Equal(1, m.LastStep);
    }

    [Fact]
    public void Deadzone_SmallDeflectionDoesNothing()
    {
        var m = Opened();
        Assert.Equal(SwitchInputEvent.None, m.Update(true, 0.59f, true, Current, Count));
        Assert.Equal(SwitchInputEvent.None, m.Update(true, -0.59f, true, Current, Count));
        Assert.Equal(Current, m.Highlight);
        Assert.Equal(SwitchInputEvent.Moved, m.Update(true, SwitchInputMachine.PressThreshold, true, Current, Count));
    }

    [Fact]
    public void Hysteresis_NeedsRearmBelowLowerThreshold()
    {
        var m = Opened();
        m.Update(true, 1f, true, Current, Count);
        // 0.4 is below the press threshold but above the re-arm threshold: not re-armed.
        m.Update(true, 0.4f, true, Current, Count);
        Assert.Equal(SwitchInputEvent.None, m.Update(true, 1f, true, Current, Count));
        Assert.Equal(1, m.Highlight);
    }

    [Fact]
    public void StickPushedWhenOpening_MustRecentreFirst()
    {
        var m = new SwitchInputMachine();
        m.Update(false, 1f, true, Current, Count);
        Assert.Equal(SwitchInputEvent.Opened, m.Update(true, 1f, true, Current, Count));
        Assert.Equal(SwitchInputEvent.None, m.Update(true, 1f, true, Current, Count));
        m.Update(true, 0f, true, Current, Count);
        Assert.Equal(SwitchInputEvent.Moved, m.Update(true, 1f, true, Current, Count));
    }

    [Fact]
    public void ClampsAtEnds_NoWrap()
    {
        var m = Opened();
        for (var i = 0; i < 5; i++)
        {
            m.Update(true, 1f, true, Current, Count);
            m.Update(true, 0f, true, Current, Count);
        }

        Assert.Equal(0, m.Highlight);
        Assert.Equal(SwitchInputEvent.None, m.Update(true, 1f, true, Current, Count));

        for (var i = 0; i < 6; i++)
        {
            m.Update(true, 0f, true, Current, Count);
            m.Update(true, -1f, true, Current, Count);
        }

        Assert.Equal(Count - 1, m.Highlight);
    }

    [Fact]
    public void ReleaseOnOther_Commits()
    {
        var m = Opened();
        m.Update(true, 1f, true, Current, Count);
        Assert.Equal(SwitchInputEvent.Committed, m.Update(false, 0f, true, Current, Count));
        Assert.Equal(1, m.CommittedIndex);
        Assert.False(m.IsOpen);
    }

    [Fact]
    public void ReleaseOnCurrent_Cancels()
    {
        var m = Opened();
        m.Update(true, 1f, true, Current, Count);
        m.Update(true, 0f, true, Current, Count);
        m.Update(true, -1f, true, Current, Count); // back to current
        Assert.Equal(SwitchInputEvent.Cancelled, m.Update(false, 0f, true, Current, Count));
        Assert.Equal(-1, m.CommittedIndex);
    }

    [Fact]
    public void SingleDifficulty_ReleaseCancels()
    {
        var m = new SwitchInputMachine();
        m.Update(false, 0f, true, 0, 1);
        Assert.Equal(SwitchInputEvent.Opened, m.Update(true, 0f, true, 0, 1));
        Assert.Equal(SwitchInputEvent.None, m.Update(true, 1f, true, 0, 1));
        Assert.Equal(SwitchInputEvent.Cancelled, m.Update(false, 0f, true, 0, 1));
    }

    [Fact]
    public void NotAllowedWhileOpen_Cancels_AndNeedsNewPress()
    {
        var m = Opened();
        m.Update(true, 1f, true, Current, Count);
        Assert.Equal(SwitchInputEvent.Cancelled, m.Update(true, 0f, false, Current, Count));
        Assert.False(m.IsOpen);
        Assert.Equal(SwitchInputEvent.None, m.Update(true, 0f, false, Current, Count));
        Assert.Equal(SwitchInputEvent.None, m.Update(true, 0f, true, Current, Count));
        Assert.Equal(SwitchInputEvent.None, m.Update(false, 0f, true, Current, Count));
        Assert.Equal(SwitchInputEvent.Opened, m.Update(true, 0f, true, Current, Count));
    }

    [Fact]
    public void Paused_WorksTheSame_WhenCallerKeepsInputAllowed()
    {
        // The caller passes allowed = true while the pause menu is up; the machine has no pause state.
        var m = Opened();
        Assert.Equal(SwitchInputEvent.Moved, m.Update(true, -1f, true, Current, Count));
        Assert.Equal(SwitchInputEvent.Committed, m.Update(false, 0f, true, Current, Count));
        Assert.Equal(3, m.CommittedIndex);
    }

    [Fact]
    public void InvalidCurrentOrCount_ClosesAndIgnores()
    {
        var m = Opened();
        Assert.Equal(SwitchInputEvent.Cancelled, m.Update(true, 0f, true, -1, Count));
        Assert.Equal(SwitchInputEvent.None, m.Update(false, 0f, true, 0, 0));
        Assert.Equal(SwitchInputEvent.None, m.Update(true, 0f, true, 0, 0));
        Assert.False(m.IsOpen);
    }

    [Fact]
    public void NaNStick_CountsAsCentred()
    {
        var m = Opened();
        m.Update(true, 1f, true, Current, Count);
        m.Update(true, float.NaN, true, Current, Count);
        Assert.Equal(SwitchInputEvent.Moved, m.Update(true, 1f, true, Current, Count));
        Assert.Equal(0, m.Highlight);
    }

    [Fact]
    public void ShrinkingCount_ClampsHighlight()
    {
        var m = Opened();
        m.Update(true, -1f, true, Current, Count); // row 3
        Assert.Equal(SwitchInputEvent.None, m.Update(true, 0f, true, 1, 3));
        Assert.Equal(2, m.Highlight);
    }

    [Fact]
    public void Reset_ClosesSilently()
    {
        var m = Opened();
        m.Reset();
        Assert.False(m.IsOpen);
        Assert.Equal(SwitchInputEvent.None, m.Update(false, 0f, true, Current, Count));
    }
}
