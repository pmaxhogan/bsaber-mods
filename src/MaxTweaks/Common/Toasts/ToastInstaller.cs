using Zenject;

namespace MaxTweaks.Common.Toasts;

/// <summary>App container: one toast service shared by the menu and gameplay (see IToastService).</summary>
internal class ToastInstaller : Installer
{
    public override void InstallBindings()
    {
        Container.BindInterfacesAndSelfTo<ToastService>().AsSingle();
    }
}
