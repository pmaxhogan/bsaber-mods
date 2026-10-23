using MaxTweaks.Common.Playback;
using MaxTweaks.Configuration;
using Zenject;

namespace MaxTweaks.Features.DifficultySwitch;

/// <summary>
/// Standard gameplay, practice mode only: hold-Y difficulty list and the in-place swap. Binds
/// nothing when the feature is off or the level is not started in practice mode.
/// </summary>
internal class DifficultySwitchPlayerInstaller : Installer
{
    private readonly DifficultySwitchConfig _config;
    private readonly GameplayCoreSceneSetupData _setupData;

    public DifficultySwitchPlayerInstaller(DifficultySwitchConfig config, GameplayCoreSceneSetupData setupData)
    {
        _config = config;
        _setupData = setupData;
    }

    public override void InstallBindings()
    {
        if (!_config.Enabled || _setupData.practiceSettings == null)
        {
            return;
        }

        PlaybackInstaller.BindPlaybackSeeker(Container);
        Container.Bind<HeckDataRefresher>().AsSingle();
        Container.Bind<InPlaceSwapper>().AsSingle();
        Container.Bind<FallbackRestarter>().AsSingle();
        Container.BindInterfacesAndSelfTo<BeatmapPreloader>().AsSingle();
        Container.BindInterfacesAndSelfTo<NoteSlider>().AsSingle();
        Container.BindInterfacesTo<DifficultySwitchController>().AsSingle();
    }
}
