using MaxTweaks.Common;
using MaxTweaks.Common.Playback;
using MaxTweaks.Configuration;
using MaxTweaks.Core.Chapters;
using Zenject;

namespace MaxTweaks.Features.Chapters;

/// <summary>
/// Gameplay: chapter display above the notes, plus skip arrows in practice mode (during play and in
/// the pause menu). Binds nothing when the feature is off or the map has no chapters.json, so maps
/// without chapters cost nothing.
/// </summary>
internal class ChaptersPlayerInstaller : Installer
{
    private readonly ChaptersConfig _config;
    private readonly GameplayCoreSceneSetupData _setupData;

    public ChaptersPlayerInstaller(ChaptersConfig config, GameplayCoreSceneSetupData setupData)
    {
        _config = config;
        _setupData = setupData;
    }

    public override void InstallBindings()
    {
        if (!_config.Enabled)
        {
            return;
        }

        var document = Guard.Run("Chapters", () => ChaptersLoader.Load(_setupData.beatmapLevel), null);
        if (document == null || document.Chapters.Count == 0)
        {
            return;
        }

        Container.Bind<ChaptersDocument>().FromInstance(document).AsSingle();
        if (_setupData.practiceSettings != null)
        {
            PlaybackInstaller.BindPlaybackSeeker(Container);
        }

        Container.BindInterfacesTo<ChaptersController>().AsSingle();
    }
}
