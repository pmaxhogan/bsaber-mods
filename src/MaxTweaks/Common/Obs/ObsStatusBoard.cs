using System;
using MaxTweaks.Core.ObsStatus;

namespace MaxTweaks.Common.Obs;

/// <summary>
/// App lifetime: what the OBS features know about themselves, in one place, so that something
/// that shows it (the OBS status line) depends on neither feature. Each OBS feature publishes its
/// own state from its once-a-second poll. Main thread only.
/// </summary>
internal sealed class ObsStatusBoard
{
    /// <summary>Raised on the main thread when either state changed.</summary>
    public event Action? Changed;

    public ObsFeatureState Recording { get; private set; }

    public ObsFeatureState Clips { get; private set; }

    public void SetRecording(ObsFeatureState state)
    {
        if (state != Recording)
        {
            Recording = state;
            Changed?.Invoke();
        }
    }

    public void SetClips(ObsFeatureState state)
    {
        if (state != Clips)
        {
            Clips = state;
            Changed?.Invoke();
        }
    }
}
