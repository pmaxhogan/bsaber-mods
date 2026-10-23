using MaxTweaks.Core.Blink;
using Zenject;

namespace MaxTweaks.Features.Blink;

/// <summary>
/// App lifetime: the played-time cooldown (spec: it carries across songs) and the generated
/// overlay textures (built once, reused by every song).
/// </summary>
internal class BlinkAppInstaller : Installer
{
    public override void InstallBindings()
    {
        Container.Bind<BlinkCooldown>().AsSingle();
        Container.Bind<BlinkAssets>().AsSingle();
    }
}
