using MaxTweaks.Configuration;
using Zenject;

namespace MaxTweaks.Features.Blink;

/// <summary>
/// Solo gameplay (standard + campaign; party mode is standard): lookahead scheduler and the
/// vignette overlay. Multiplayer is excluded by the install location (see Plugin.cs); replays
/// are excluded here.
/// </summary>
internal class BlinkPlayerInstaller : Installer
{
    private readonly BlinkConfig _config;

    public BlinkPlayerInstaller(BlinkConfig config)
    {
        _config = config;
    }

    public override void InstallBindings()
    {
        if (!_config.Enabled)
        {
            return;
        }

        if (ReplayDetector.IsWatchingReplay())
        {
            Plugin.Log.Debug("[Blink] replay playback detected; blink disabled for this level");
            return;
        }

        Container.Bind(typeof(IInitializable), typeof(ITickable), typeof(System.IDisposable)).To<BlinkController>().AsSingle();
    }
}
