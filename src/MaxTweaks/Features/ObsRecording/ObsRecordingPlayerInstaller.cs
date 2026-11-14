using MaxTweaks.Configuration;
using Zenject;

namespace MaxTweaks.Features.ObsRecording;

/// <summary>
/// Every gameplay scene (solo, campaign, multiplayer; practice and replays included): one recording
/// per level. Binds nothing when the feature is off.
/// </summary>
internal class ObsRecordingPlayerInstaller : Installer
{
    private readonly ObsRecordingConfig _config;

    public ObsRecordingPlayerInstaller(ObsRecordingConfig config)
    {
        _config = config;
    }

    public override void InstallBindings()
    {
        if (!_config.Enabled)
        {
            return;
        }

        Container.BindInterfacesTo<ObsRecordingLevelHook>().AsSingle();
    }
}
