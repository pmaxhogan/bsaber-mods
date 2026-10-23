using Zenject;

namespace MaxTweaks.Common.Debugging;

// Installed by Plugin.cs only when DebugMode.Enabled (game started with --debug).

/// <summary>App container: the command file poller and virtual controller.</summary>
internal class DebugAppInstaller : Installer
{
    public override void InstallBindings()
    {
        Container.BindInterfacesAndSelfTo<DebugCommandService>().AsSingle();
    }
}

/// <summary>Menu container: play, select-level, open-settings, main-menu, levels.</summary>
internal class DebugMenuInstaller : Installer
{
    public override void InstallBindings()
    {
        Container.BindInterfacesAndSelfTo<DebugMenuContext>().AsSingle();
    }
}

/// <summary>Player container (every gameplay mode): pause, resume, restart, exit.</summary>
internal class DebugPlayerInstaller : Installer
{
    public override void InstallBindings()
    {
        Container.BindInterfacesAndSelfTo<DebugGameplayContext>().AsSingle();
    }
}
