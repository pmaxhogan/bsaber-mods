using MaxTweaks.Configuration;
using Zenject;

namespace MaxTweaks.Features.HeartRateGraph;

/// <summary>Gameplay: the heart rate graph under HRCounter's counter. Binds nothing when the feature is off.</summary>
internal class HeartRateGraphPlayerInstaller : Installer
{
    private readonly HeartRateGraphConfig _config;

    public HeartRateGraphPlayerInstaller(HeartRateGraphConfig config)
    {
        _config = config;
    }

    public override void InstallBindings()
    {
        if (!_config.Enabled)
        {
            return;
        }

        Container.BindInterfacesTo<HeartRateGraphController>().AsSingle();
    }
}
