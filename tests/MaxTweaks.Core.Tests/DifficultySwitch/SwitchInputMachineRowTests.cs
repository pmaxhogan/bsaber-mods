using MaxTweaks.Core.DifficultySwitch;
using Xunit;

namespace MaxTweaks.Core.Tests.DifficultySwitch;

public class SwitchInputMachineRowTests
{
    private const int Count = 4;
    private const int Current = 2;
    private const int RowCount = 23;
    private const int RowCurrent = 8;

    private static SwitchInputEvent U(SwitchInputMachine m, bool y, float x, float sy, bool allowed = true, int rowCount = RowCount, int rowCurrent = RowCurrent) =>
        m.Update(y, x, sy, allowed, Current, Count, rowCount, rowCurrent);

    private static SwitchInputMachine Opened(int rowCount = RowCount)
    {
        var m = new SwitchInputMachine();
        Assert.Equal(SwitchInputEvent.None, U(m, false, 0f, 0f, rowCount: rowCount));
        Assert.Equal(SwitchInputEvent.Opened, U(m, true, 0f, 0f, rowCount: rowCount));
        Assert.False(m.RowOpen);
        return m;
    }

    [Fact]
    public void FirstHorizontalFlick_OpensRow_AndMovesOne()
    {
        var m = Opened();
        Assert.Equal(SwitchInputEvent.RowOpened, U(m, true, 1f, 0f));
        Assert.True(m.RowOpen);
        Assert.Equal(RowCurrent + 1, m.RowHighlight);
        Assert.Equal(1, m.LastRowStep);
    }

    [Fact]
    public void LeftFlick_MovesDown_NoRepeatWhileHeld()
    {
        var m = Opened();
        Assert.Equal(SwitchInputEvent.RowOpened, U(m, true, -1f, 0f));
        Assert.Equal(RowCurrent - 1, m.RowHighlight);
        Assert.Equal(SwitchInputEvent.None, U(m, true, -1f, 0f));
        Assert.Equal(SwitchInputEvent.None, U(m, true, -0.4f, 0f));
        Assert.Equal(SwitchInputEvent.None, U(m, true, -0.2f, 0f));
        Assert.Equal(SwitchInputEvent.RowMoved, U(m, true, -0.8f, 0f));
        Assert.Equal(RowCurrent - 2, m.RowHighlight);
        Assert.Equal(-1, m.LastRowStep);
    }

    [Fact]
    public void Row_StopsAtBothEnds()
    {
        var m = Opened(rowCount: 3);
        m.Update(true, 1f, 0f, true, Current, Count, 3, 1);
        Assert.Equal(2, m.RowHighlight);
        m.Update(true, 0f, 0f, true, Current, Count, 3, 1);
        Assert.Equal(SwitchInputEvent.None, m.Update(true, 1f, 0f, true, Current, Count, 3, 1));
        Assert.Equal(2, m.RowHighlight);
        for (var i = 0; i < 4; i++)
        {
            m.Update(true, 0f, 0f, true, Current, Count, 3, 1);
            m.Update(true, -1f, 0f, true, Current, Count, 3, 1);
        }

        Assert.Equal(0, m.RowHighlight);
    }

    [Fact]
    public void FirstFlickAtEnd_StillOpensRow()
    {
        var m = new SwitchInputMachine();
        m.Update(false, 0f, 0f, true, Current, Count, 3, 2);
        m.Update(true, 0f, 0f, true, Current, Count, 3, 2);
        Assert.Equal(SwitchInputEvent.RowOpened, m.Update(true, 1f, 0f, true, Current, Count, 3, 2));
        Assert.True(m.RowOpen);
        Assert.Equal(2, m.RowHighlight);
        Assert.Equal(0, m.LastRowStep);
    }

    [Fact]
    public void ReleaseOnOtherValue_CommitsRow()
    {
        var m = Opened();
        U(m, true, 1f, 0f);
        U(m, true, 0f, 0f);
        U(m, true, 1f, 0f);
        Assert.Equal(SwitchInputEvent.RowCommitted, U(m, false, 0f, 0f));
        Assert.Equal(RowCurrent + 2, m.CommittedRowIndex);
        Assert.False(m.IsOpen);
        Assert.False(m.RowOpen);
    }

    [Fact]
    public void ReleaseOnCurrentValue_Cancels()
    {
        var m = Opened();
        U(m, true, 1f, 0f);
        U(m, true, 0f, 0f);
        U(m, true, -1f, 0f);
        Assert.Equal(RowCurrent, m.RowHighlight);
        Assert.Equal(SwitchInputEvent.Cancelled, U(m, false, 0f, 0f));
    }

    [Fact]
    public void VerticalOnOtherDifficulty_WinsOverRow()
    {
        var m = Opened();
        U(m, true, 1f, 0f);
        U(m, true, 0f, 0f);
        Assert.Equal(SwitchInputEvent.Moved, U(m, true, 0f, 1f));
        Assert.Equal(SwitchInputEvent.Committed, U(m, false, 0f, 0f));
        Assert.Equal(1, m.CommittedIndex);
    }

    [Fact]
    public void VerticalStillWorks_WithRowOpen()
    {
        var m = Opened();
        U(m, true, 1f, 0f);
        U(m, true, 0f, 0f);
        Assert.Equal(SwitchInputEvent.Moved, U(m, true, 0f, -1f));
        Assert.Equal(3, m.Highlight);
        Assert.True(m.RowOpen);
    }

    [Fact]
    public void AxesArmIndependently()
    {
        var m = Opened();
        // Vertical held: a horizontal flick still counts.
        Assert.Equal(SwitchInputEvent.Moved, U(m, true, 0f, 1f));
        Assert.Equal(SwitchInputEvent.RowOpened, U(m, true, 1f, 0f));
        Assert.Equal(1, m.Highlight);
        Assert.Equal(RowCurrent + 1, m.RowHighlight);
    }

    [Fact]
    public void Diagonal_OnlyDominantAxisCounts_BothMustRecentre()
    {
        var m = Opened();
        Assert.Equal(SwitchInputEvent.RowOpened, U(m, true, 0.9f, 0.7f));
        Assert.Equal(Current, m.Highlight);
        Assert.Equal(SwitchInputEvent.None, U(m, true, 0f, 0.7f));
        Assert.Equal(Current, m.Highlight);
        U(m, true, 0f, 0f);
        Assert.Equal(SwitchInputEvent.Moved, U(m, true, 0f, 0.7f));
    }

    [Fact]
    public void StickPushedSidewaysWhenOpening_MustRecentre()
    {
        var m = new SwitchInputMachine();
        U(m, false, 1f, 0f);
        Assert.Equal(SwitchInputEvent.Opened, U(m, true, 1f, 0f));
        Assert.Equal(SwitchInputEvent.None, U(m, true, 1f, 0f));
        U(m, true, 0f, 0f);
        Assert.Equal(SwitchInputEvent.RowOpened, U(m, true, 1f, 0f));
    }

    [Fact]
    public void NoRow_HorizontalFlickReportsUnavailable_EachFlick()
    {
        var m = Opened(rowCount: 0);
        Assert.Equal(SwitchInputEvent.RowUnavailable, U(m, true, 1f, 0f, rowCount: 0));
        Assert.Equal(SwitchInputEvent.None, U(m, true, 1f, 0f, rowCount: 0));
        U(m, true, 0f, 0f, rowCount: 0);
        Assert.Equal(SwitchInputEvent.RowUnavailable, U(m, true, -1f, 0f, rowCount: 0));
        Assert.False(m.RowOpen);
        Assert.Equal(SwitchInputEvent.Cancelled, U(m, false, 0f, 0f, rowCount: 0));
    }

    [Fact]
    public void RowCurrentOutOfRange_TreatedAsNoRow()
    {
        var m = Opened();
        Assert.Equal(SwitchInputEvent.RowUnavailable, U(m, true, 1f, 0f, rowCurrent: RowCount));
    }

    [Fact]
    public void RowShrinksWhileOpen_ClosesRow()
    {
        var m = Opened();
        U(m, true, 1f, 0f);
        Assert.Equal(SwitchInputEvent.None, U(m, true, 0f, 0f, rowCount: 0));
        Assert.False(m.RowOpen);
        Assert.Equal(SwitchInputEvent.Cancelled, U(m, false, 0f, 0f, rowCount: 0));
    }

    [Fact]
    public void ReopeningList_StartsWithRowClosed()
    {
        var m = Opened();
        U(m, true, 1f, 0f);
        U(m, false, 0f, 0f);
        Assert.Equal(SwitchInputEvent.Opened, U(m, true, 0f, 0f));
        Assert.False(m.RowOpen);
        Assert.Equal(SwitchInputEvent.RowOpened, U(m, true, -1f, 0f));
        Assert.Equal(RowCurrent - 1, m.RowHighlight);
    }

    [Fact]
    public void NotAllowed_ClosesRowToo()
    {
        var m = Opened();
        U(m, true, 1f, 0f);
        Assert.Equal(SwitchInputEvent.Cancelled, U(m, true, 0f, 0f, allowed: false));
        Assert.False(m.RowOpen);
    }

    [Fact]
    public void NaNStickX_CountsAsCentred()
    {
        var m = Opened();
        U(m, true, 1f, 0f);
        U(m, true, float.NaN, 0f);
        Assert.Equal(SwitchInputEvent.RowMoved, U(m, true, 1f, 0f));
    }

    [Fact]
    public void Reset_ClosesRow()
    {
        var m = Opened();
        U(m, true, 1f, 0f);
        m.Reset();
        Assert.False(m.IsOpen);
        Assert.False(m.RowOpen);
    }
}
