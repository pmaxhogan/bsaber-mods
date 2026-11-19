using MaxTweaks.Configuration;
using Zenject;

namespace MaxTweaks.Features.ObsClips;

/// <summary>
/// Every gameplay scene (solo, campaign, multiplayer; practice and replays included). Binds nothing
/// when the feature is off.
/// </summary>
internal class ObsClipsPlayerInstaller : Installer
{
    private readonly ObsClipsConfig _config;

    public ObsClipsPlayerInstaller(ObsClipsConfig config)
    {
        _config = config;
    }

    public override void InstallBindings()
    {
        if (!_config.Enabled)
        {
            return;
        }

        Container.BindInterfacesTo<ObsClipsLevelHook>().AsSingle();
    }
}
