using MaxTweaks.Core.AutoReload;

namespace MaxTweaks.Core.Tests.AutoReload;

internal sealed class FakeClock : IClock
{
    public double Now { get; set; }

    public void Advance(double seconds) => Now += seconds;
}
