namespace MaxTweaks.Core.ActivePause;

/// <summary>What a press of the toggle button should do.</summary>
public enum ToggleAction
{
    /// <summary>Nothing (the press was refused; see <see cref="ActivePauseToggle.LastRefusal"/>).</summary>
    None,

    /// <summary>Enter active pause: pause the game without the pause menu.</summary>
    Activate,

    /// <summary>Leave active pause: resume the game.</summary>
    Deactivate,
}

/// <summary>
/// Active-pause on/off state, independent of the game.
///
/// Rules:
/// - A toggle press while active always deactivates.
/// - A toggle press while inactive activates only when the game is not paused by anything else (the
///   pause menu, its resume countdown) and the song can be seeked (running, not finished).
/// - When the game's own pause menu opens while active, active pause is dropped: the game owns the
///   pause from then on and resumes through its Continue button, so the caller must not resume.
/// - A failure in the caller resets the state (<see cref="Reset"/>).
/// </summary>
public sealed class ActivePauseToggle
{
    public bool IsActive { get; private set; }

    /// <summary>Why the last refused press was refused, for the log.</summary>
    public string? LastRefusal { get; private set; }

    /// <param name="gamePaused">The game is paused right now (IGamePause.isPaused).</param>
    /// <param name="canSeek">The song is running or paused and not finished.</param>
    public ToggleAction OnTogglePressed(bool gamePaused, bool canSeek)
    {
        LastRefusal = null;
        if (IsActive)
        {
            IsActive = false;
            return ToggleAction.Deactivate;
        }

        if (gamePaused)
        {
            LastRefusal = "game already paused";
            return ToggleAction.None;
        }

        if (!canSeek)
        {
            LastRefusal = "song not running";
            return ToggleAction.None;
        }

        IsActive = true;
        return ToggleAction.Activate;
    }

    /// <summary>The game's pause menu opened. Returns true when active pause was dropped by it.</summary>
    public bool OnPauseMenuOpened()
    {
        if (!IsActive)
        {
            return false;
        }

        IsActive = false;
        return true;
    }

    public void Reset() => IsActive = false;
}
