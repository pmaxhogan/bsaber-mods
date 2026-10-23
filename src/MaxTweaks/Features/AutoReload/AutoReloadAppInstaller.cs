using Zenject;

namespace MaxTweaks.Features.AutoReload;

/// <summary>App lifetime: the file watcher (it must survive menu rebuilds and keep queuing while in gameplay).</summary>
internal class AutoReloadAppInstaller : Installer
{
    public override void InstallBindings()
    {
        Container.BindInterfacesAndSelfTo<AutoReloadService>().AsSingle();
    }
}
