using Zenject;

namespace MaxTweaks.Features.HeartRateBridge;

/// <summary>App lifetime: the bridge process must start with the game and survive menu rebuilds.</summary>
internal class HeartRateBridgeAppInstaller : Installer
{
    public override void InstallBindings()
    {
        Container.BindInterfacesAndSelfTo<HeartRateBridgeService>().AsSingle();
    }
}
