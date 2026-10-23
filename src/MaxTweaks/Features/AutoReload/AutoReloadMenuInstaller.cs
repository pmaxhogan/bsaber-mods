using Zenject;

namespace MaxTweaks.Features.AutoReload;

/// <summary>Menu scene: run queued reloads, keep the list position, show the summary toast.</summary>
internal class AutoReloadMenuInstaller : Installer
{
    public override void InstallBindings()
    {
        Container.BindInterfacesAndSelfTo<LevelPlaceKeeper>().AsSingle();
        Container.BindInterfacesAndSelfTo<MenuReloadRunner>().AsSingle();
    }
}
