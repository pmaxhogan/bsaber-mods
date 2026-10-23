using MaxTweaks.Common;
using MaxTweaks.Configuration;
using Zenject;

namespace MaxTweaks.Installers;

internal class AppInstaller : Installer
{
    private readonly PluginConfig _config;

    public AppInstaller(PluginConfig config)
    {
        _config = config;
    }

    public override void InstallBindings()
    {
        Container.BindInstance(_config);
        Container.BindInstance(_config.HideOst);
        Container.BindInstance(_config.AutoReload);
        Container.BindInstance(_config.Blink);
        Container.BindInstance(_config.Chapters);
        Container.BindInstance(_config.DifficultySwitch);
        Container.Bind<FeatureStatus>().AsSingle();
        Container.Bind<SafePatcher>().AsSingle();
    }
}
