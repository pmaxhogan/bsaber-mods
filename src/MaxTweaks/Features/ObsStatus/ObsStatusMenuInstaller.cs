using Zenject;

namespace MaxTweaks.Features.ObsStatus;

/// <summary>Menu scene: the status line on the song selection screen.</summary>
internal class ObsStatusMenuInstaller : Installer
{
    public override void InstallBindings()
    {
        Container.BindInterfacesTo<ObsStatusPanel>().AsSingle();
    }
}
