using MaxTweaks.Features.ActivePause;
using MaxTweaks.Features.AutoReload;
using MaxTweaks.Features.Blink;
using MaxTweaks.Features.Chapters;
using MaxTweaks.Features.DifficultySwitch;
using MaxTweaks.Features.HeartRateBridge;
using MaxTweaks.Features.HideOst;
using MaxTweaks.UI;
using Zenject;

namespace MaxTweaks.Installers;

/// <summary>Shared menu bindings: the settings page and the quick-toggle tab.</summary>
internal class MenuInstaller : Installer
{
    public override void InstallBindings()
    {
        Container.Bind<HideOstSettingsHost>().AsSingle();
        Container.Bind<AutoReloadSettingsHost>().AsSingle();
        Container.Bind<BlinkSettingsHost>().AsSingle();
        Container.Bind<ChaptersSettingsHost>().AsSingle();
        Container.Bind<DifficultySwitchSettingsHost>().AsSingle();
        Container.Bind<ActivePauseSettingsHost>().AsSingle();
        Container.BindInterfacesAndSelfTo<HeartRateBridgeSettingsHost>().AsSingle();
        Container.BindInterfacesAndSelfTo<SettingsMenuRegistrar>().AsSingle();
    }
}
