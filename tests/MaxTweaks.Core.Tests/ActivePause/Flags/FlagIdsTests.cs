using System;
using MaxTweaks.Core.ActivePause.Flags;
using Xunit;

namespace MaxTweaks.Core.Tests.ActivePause.Flags;

public class FlagIdsTests
{
    [Fact]
    public void New_FormatsTimestampAndHex()
    {
        var id = FlagIds.New(new DateTime(2026, 10, 25, 19, 30, 12, DateTimeKind.Utc), new Random(1));
        Assert.Matches(@"^20261025-193012-[0-9a-f]{4}$", id);
    }

    [Fact]
    public void New_DiffersWithRandom()
    {
        var at = new DateTime(2026, 10, 25, 19, 30, 12, DateTimeKind.Utc);
        var random = new Random(7);
        Assert.NotEqual(FlagIds.New(at, random), FlagIds.New(at, random));
    }
}
