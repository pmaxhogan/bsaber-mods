using Zenject;

namespace MaxTweaks.Common.Playback;

/// <summary>Binding helpers for <see cref="PlaybackSeeker"/>.</summary>
internal static class PlaybackInstaller
{
    /// <summary>
    /// Binds <see cref="PlaybackSeeker"/> and <see cref="ActiveBeatmap"/> as single instances in this
    /// (player) container unless a feature already did. Call from a feature's player installer, only
    /// in practice mode (GameplayCoreSceneSetupData.practiceSettings != null). Binding twice would make
    /// Zenject throw, which is why every feature must go through here.
    /// </summary>
    public static void BindPlaybackSeeker(DiContainer container)
    {
        if (!container.HasBinding<PlaybackSeeker>())
        {
            container.Bind<PlaybackSeeker>().AsSingle();
        }

        if (!container.HasBinding<ActiveBeatmap>())
        {
            container.Bind<ActiveBeatmap>().AsSingle();
        }
    }
}
