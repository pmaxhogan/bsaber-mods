using MaxTweaks.Common.Playback;
using MaxTweaks.Configuration;
using Zenject;

namespace MaxTweaks.Features.ActivePause;

/// <summary>
/// Standard gameplay, practice mode only: X pauses in place without the pause menu and shows the
/// seek buttons. Binds nothing when the feature is off or the level is not started in practice mode.
/// </summary>
internal class ActivePausePlayerInstaller : Installer
{
    private readonly ActivePauseConfig _config;
    private readonly GameplayCoreSceneSetupData _setupData;

    public ActivePausePlayerInstaller(ActivePauseConfig config, GameplayCoreSceneSetupData setupData)
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
        Container.BindInterfacesTo<ActivePauseController>().AsSingle();
    }
}
