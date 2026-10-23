using Zenject;

namespace MaxTweaks.Features.DifficultySwitch;

/// <summary>App container: the level-start argument capture used by the restart fallback.</summary>
internal class DifficultySwitchAppInstaller : Installer
{
    public override void InstallBindings()
    {
        Container.BindInterfacesTo<TransitionInitCaptureInstaller>().AsSingle();
    }
}
