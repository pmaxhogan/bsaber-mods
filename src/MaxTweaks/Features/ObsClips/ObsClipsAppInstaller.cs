using Zenject;

namespace MaxTweaks.Features.ObsClips;

/// <summary>App lifetime: the OBS connection and the clip budget outlive level scenes and menu rebuilds.</summary>
internal class ObsClipsAppInstaller : Installer
{
    public override void InstallBindings()
    {
        Container.BindInterfacesAndSelfTo<ObsClipsService>().AsSingle();
    }
}
