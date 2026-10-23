using System.Runtime.CompilerServices;
using IPA;
using IPA.Config.Stores;
using MaxTweaks.Common.Debugging;
using MaxTweaks.Configuration;
using MaxTweaks.Features.AutoReload;
using MaxTweaks.Features.Blink;
using MaxTweaks.Features.Chapters;
using MaxTweaks.Features.DifficultySwitch;
using MaxTweaks.Features.HideOst;
using MaxTweaks.Installers;
using SiraUtil.Zenject;
using IPAConfig = IPA.Config.Config;
using IPALogger = IPA.Logging.Logger;

[assembly: InternalsVisibleTo(GeneratedStore.AssemblyVisibilityTarget)]

namespace MaxTweaks;

/// <summary>
/// Entry point. Kept deliberately tiny: it only wires installers. Each feature owns its own
/// installers under Features/&lt;Name&gt;/ and checks its own Enabled flag, so one broken
/// feature never takes the others down.
/// </summary>
[Plugin(RuntimeOptions.SingleStartInit)]
public class Plugin
{
    internal static IPALogger Log { get; private set; } = null!;

    [Init]
    public Plugin(IPALogger logger, IPAConfig config, Zenjector zenjector)
    {
        Log = logger;
        var pluginConfig = config.Generated<PluginConfig>();

        zenjector.UseLogger(logger);

        zenjector.Install<AppInstaller>(Location.App, pluginConfig);
        zenjector.Install<MenuInstaller>(Location.Menu);

        // Feature installers. Locations are chosen per feature; see each installer.
        zenjector.Install<HideOstMenuInstaller>(Location.Menu);
        zenjector.Install<AutoReloadAppInstaller>(Location.App);
        zenjector.Install<AutoReloadMenuInstaller>(Location.Menu);
        zenjector.Install<AutoReloadPlayerInstaller>(Location.Player);
        zenjector.Install<BlinkPlayerInstaller>(Location.StandardPlayer | Location.CampaignPlayer);
        zenjector.Install<ChaptersPlayerInstaller>(Location.Player);
        zenjector.Install<DifficultySwitchPlayerInstaller>(Location.StandardPlayer);

        // QA debug command channel (docs/qa.md): only bound when the game runs with --debug.
        if (DebugMode.Enabled)
        {
            zenjector.Install<DebugAppInstaller>(Location.App);
            zenjector.Install<DebugMenuInstaller>(Location.Menu);
            zenjector.Install<DebugPlayerInstaller>(Location.Player);
        }
    }

    [OnStart]
    public void OnApplicationStart()
    {
    }

    [OnExit]
    public void OnApplicationQuit()
    {
    }
}
