using MaxTweaks.Core.AutoReload;
using Xunit;

namespace MaxTweaks.Core.Tests.AutoReload;

public class SidecarWriteGuardTests
{
    [Fact]
    public void Mark_SuppressesTheFolderForTheWindow()
    {
        var clock = new FakeClock { Now = 100 };
        var guard = new SidecarWriteGuard(clock);
        Assert.False(guard.IsSuppressed(@"C:\Levels\Map"));

        guard.Mark(@"C:\Levels\Map");
        Assert.True(guard.IsSuppressed(@"c:/levels/map/"));
        Assert.False(guard.IsSuppressed(@"C:\Levels\Other"));

        clock.Advance(SidecarWriteGuard.WindowSeconds - 0.1);
        Assert.True(guard.IsSuppressed(@"C:\Levels\Map"));

        clock.Advance(0.2);
        Assert.False(guard.IsSuppressed(@"C:\Levels\Map"));
    }

    [Fact]
    public void EmptyFolder_IsIgnored()
    {
        var guard = new SidecarWriteGuard(new FakeClock());
        guard.Mark(string.Empty);
        Assert.False(guard.IsSuppressed(string.Empty));
    }
}
