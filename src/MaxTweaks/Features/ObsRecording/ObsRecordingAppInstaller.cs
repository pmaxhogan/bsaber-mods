using Zenject;

namespace MaxTweaks.Features.ObsRecording;

/// <summary>App lifetime: the OBS connection outlives level scenes and menu rebuilds.</summary>
internal class ObsRecordingAppInstaller : Installer
{
    public override void InstallBindings()
    {
        Container.BindInterfacesAndSelfTo<ObsRecordingService>().AsSingle();
    }
}
