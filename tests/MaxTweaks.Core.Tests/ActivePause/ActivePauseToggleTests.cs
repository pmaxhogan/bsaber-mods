using MaxTweaks.Core.ActivePause;
using Xunit;

namespace MaxTweaks.Core.Tests.ActivePause;

public class ActivePauseToggleTests
{
    [Fact]
    public void StartsInactive()
    {
        var toggle = new ActivePauseToggle();
        Assert.False(toggle.IsActive);
        Assert.Null(toggle.LastRefusal);
    }

    [Fact]
    public void PressWhilePlaying_ActivatesThenSecondPressDeactivates()
    {
        var toggle = new ActivePauseToggle();
        Assert.Equal(ToggleAction.Activate, toggle.OnTogglePressed(gamePaused: false, canSeek: true));
        Assert.True(toggle.IsActive);

        // While active the game reports paused (we paused it): the press still deactivates.
        Assert.Equal(ToggleAction.Deactivate, toggle.OnTogglePressed(gamePaused: true, canSeek: true));
        Assert.False(toggle.IsActive);
        Assert.Null(toggle.LastRefusal);
    }

    [Fact]
    public void DeactivatesEvenWhenSeekingIsNoLongerPossible()
    {
        var toggle = new ActivePauseToggle();
        toggle.OnTogglePressed(false, true);
        Assert.Equal(ToggleAction.Deactivate, toggle.OnTogglePressed(true, false));
    }

    [Fact]
    public void PressWhileGamePaused_IsRefused()
    {
        var toggle = new ActivePauseToggle();
        Assert.Equal(ToggleAction.None, toggle.OnTogglePressed(gamePaused: true, canSeek: true));
        Assert.False(toggle.IsActive);
        Assert.Equal("game already paused", toggle.LastRefusal);
    }

    [Fact]
    public void PressWhenSongNotRunning_IsRefused()
    {
        var toggle = new ActivePauseToggle();
        Assert.Equal(ToggleAction.None, toggle.OnTogglePressed(gamePaused: false, canSeek: false));
        Assert.False(toggle.IsActive);
        Assert.Equal("song not running", toggle.LastRefusal);

        // The refusal is cleared by the next accepted press.
        Assert.Equal(ToggleAction.Activate, toggle.OnTogglePressed(false, true));
        Assert.Null(toggle.LastRefusal);
    }

    [Fact]
    public void PauseMenuWhileActive_DropsWithoutResume()
    {
        var toggle = new ActivePauseToggle();
        toggle.OnTogglePressed(false, true);
        Assert.True(toggle.OnPauseMenuOpened());
        Assert.False(toggle.IsActive);

        // The pause menu now owns the pause: a press during it is refused, not a resume.
        Assert.Equal(ToggleAction.None, toggle.OnTogglePressed(gamePaused: true, canSeek: true));

        // After Continue the game is unpaused again and X works as usual.
        Assert.Equal(ToggleAction.Activate, toggle.OnTogglePressed(gamePaused: false, canSeek: true));
        Assert.Equal(ToggleAction.Deactivate, toggle.OnTogglePressed(gamePaused: true, canSeek: true));
    }

    [Fact]
    public void PauseMenuWhileInactive_IsNotADrop()
    {
        var toggle = new ActivePauseToggle();
        Assert.False(toggle.OnPauseMenuOpened());
        Assert.False(toggle.IsActive);
    }

    [Fact]
    public void Reset_Deactivates()
    {
        var toggle = new ActivePauseToggle();
        toggle.OnTogglePressed(false, true);
        toggle.Reset();
        Assert.False(toggle.IsActive);
        Assert.Equal(ToggleAction.Activate, toggle.OnTogglePressed(false, true));
    }
}
