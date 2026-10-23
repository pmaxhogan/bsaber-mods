using System;
using MaxTweaks.Core.DifficultySwitch;
using Xunit;

namespace MaxTweaks.Core.Tests.DifficultySwitch;

public class DifficultyOrderTests
{
    [Fact]
    public void HardestFirst_SortsDescending_Distinct()
    {
        Assert.Equal(new[] { 4, 3, 1, 0 }, DifficultyOrder.HardestFirst(new[] { 1, 4, 0, 3, 4 }));
        Assert.Empty(DifficultyOrder.HardestFirst(Array.Empty<int>()));
        Assert.Throws<ArgumentNullException>(() => DifficultyOrder.HardestFirst(null!));
    }

    [Fact]
    public void IndexOf_FindsRank()
    {
        var order = DifficultyOrder.HardestFirst(new[] { 2, 3, 4 });
        Assert.Equal(0, DifficultyOrder.IndexOf(order, 4));
        Assert.Equal(2, DifficultyOrder.IndexOf(order, 2));
        Assert.Equal(-1, DifficultyOrder.IndexOf(order, 0));
    }

    [Fact]
    public void Labels()
    {
        Assert.Equal("Easy", DifficultyOrder.DefaultLabel(0));
        Assert.Equal("Expert+", DifficultyOrder.DefaultLabel(4));
        Assert.Equal("Difficulty 7", DifficultyOrder.DefaultLabel(7));
        Assert.Equal("Difficulty -1", DifficultyOrder.DefaultLabel(-1));
        Assert.Equal("Hard", DifficultyOrder.Label(2, null));
        Assert.Equal("Hard", DifficultyOrder.Label(2, "  "));
        Assert.Equal("Chill", DifficultyOrder.Label(2, " Chill "));
    }
}

public class ClearGapTests
{
    [Theory]
    [InlineData(3f, 3f)]
    [InlineData(-1f, 0f)]
    [InlineData(25f, 10f)]
    [InlineData(float.NaN, 3f)]
    public void Clamp(float input, float expected) => Assert.Equal(expected, ClearGap.Clamp(input));

    [Fact]
    public void ObjectsFromTime_IsGapAfterSwitch()
    {
        Assert.Equal(43f, ClearGap.ObjectsFromTime(40f, 3f, 1.2f), 3);
    }

    [Fact]
    public void ObjectsFromTime_AtLeastOneSpawnAheadPlusMargin()
    {
        // A short gap would make the first new object pop up mid-flight; the window grows instead.
        Assert.Equal(40f + 1.5f + ClearGap.SpawnMargin, ClearGap.ObjectsFromTime(40f, 0.5f, 1.5f), 3);
        Assert.Equal(40f + ClearGap.SpawnMargin, ClearGap.ObjectsFromTime(40f, 0f, float.NaN), 3);
        Assert.Equal(40f + ClearGap.SpawnMargin, ClearGap.ObjectsFromTime(40f, 0f, -2f), 3);
    }

    [Fact]
    public void Filtering_RemovesOnlyTheWindow()
    {
        const float Switch = 40f;
        var from = ClearGap.ObjectsFromTime(Switch, 3f, 1f);
        Assert.False(ClearGap.IsCleared(39.99f, Switch, from)); // already behind the cursor, not "cleared"
        Assert.True(ClearGap.IsCleared(40f, Switch, from));
        Assert.True(ClearGap.IsCleared(42.99f, Switch, from));
        Assert.False(ClearGap.IsCleared(43f, Switch, from));
        Assert.False(ClearGap.CanSpawn(42.99f, from));
        Assert.True(ClearGap.CanSpawn(43f, from));
    }
}

public class SlideOutTests
{
    [Fact]
    public void Direction_UpWhenHarder_DownWhenEasier()
    {
        Assert.Equal(SlideDirection.Up, SlideOut.For(3, 4));
        Assert.Equal(SlideDirection.Down, SlideOut.For(4, 2));
        Assert.Equal(SlideDirection.Down, SlideOut.For(2, 2));
    }

    [Fact]
    public void Offset_EasesIn_AndClamps()
    {
        Assert.Equal(0f, SlideOut.Offset(0f, 0.3f, 3f, SlideDirection.Up));
        Assert.Equal(0.75f, SlideOut.Offset(0.15f, 0.3f, 3f, SlideDirection.Up), 3);
        Assert.Equal(3f, SlideOut.Offset(1f, 0.3f, 3f, SlideDirection.Up), 3);
        Assert.Equal(-3f, SlideOut.Offset(0.3f, 0.3f, 3f, SlideDirection.Down), 3);
        Assert.Equal(0f, SlideOut.Offset(-1f, 0.3f, 3f, SlideDirection.Down));
    }

    [Fact]
    public void Progress_EdgeCases()
    {
        Assert.Equal(1f, SlideOut.Progress(0f, 0f));
        Assert.Equal(1f, SlideOut.Progress(float.NaN, 0.3f));
        Assert.False(SlideOut.IsFinished(0.29f, 0.3f));
        Assert.True(SlideOut.IsFinished(0.3f, 0.3f));
    }
}

public class SmoothingTests
{
    [Fact]
    public void Approach_MovesTowardsTarget_AndSnaps()
    {
        var v = Smoothing.Approach(0f, 10f, 0.016f, 20f);
        Assert.InRange(v, 0.1f, 10f);
        Assert.Equal(10f, Smoothing.Approach(9.995f, 10f, 0.016f, 20f));
        Assert.Equal(-10f, Smoothing.Approach(-9.995f, -10f, 0.016f, 20f));
    }

    [Fact]
    public void Approach_IsFrameRateIndependent()
    {
        var a = 0f;
        for (var i = 0; i < 2; i++)
        {
            a = Smoothing.Approach(a, 10f, 0.01f, 5f, snap: 0f);
        }

        var b = Smoothing.Approach(0f, 10f, 0.02f, 5f, snap: 0f);
        Assert.Equal(b, a, 4);
    }

    [Fact]
    public void Approach_InvalidInputs()
    {
        Assert.Equal(3f, Smoothing.Approach(3f, 10f, 0f, 5f));
        Assert.Equal(3f, Smoothing.Approach(3f, 10f, 0.1f, 0f));
        Assert.Equal(10f, Smoothing.Approach(float.NaN, 10f, 0.1f, 5f));
    }
}

public class InPlaceGateTests
{
    [Fact]
    public void PlainMap_IsAllowed()
    {
        Assert.Null(InPlaceGate.Check(null, null, false));
        Assert.Null(InPlaceGate.Check(new[] { "", " " }, new[] { "Cinema", "Camera2 movement" }, false));
    }

    [Fact]
    public void AnyRequirement_Blocks()
    {
        Assert.Equal("requires Noodle Extensions", InPlaceGate.Check(new[] { "Noodle Extensions" }, null, false));
        Assert.Equal("requires Some New Mod", InPlaceGate.Check(new[] { " Some New Mod " }, null, false));
    }

    [Fact]
    public void BlockingSuggestions_Block()
    {
        Assert.Equal("suggests Chroma", InPlaceGate.Check(null, new[] { "Cinema", "Chroma" }, false));
        Assert.Equal("suggests mapping extensions", InPlaceGate.Check(null, new[] { "mapping extensions" }, false));
    }

    [Fact]
    public void Rotation_Blocks()
    {
        Assert.Equal("rotation (360/90) map", InPlaceGate.Check(null, null, true));
    }

    [Fact]
    public void IsBlocking()
    {
        Assert.True(InPlaceGate.IsBlocking("Vivify"));
        Assert.True(InPlaceGate.IsBlocking("Chroma Lighting Events"));
        Assert.False(InPlaceGate.IsBlocking(null));
        Assert.False(InPlaceGate.IsBlocking("Cinema"));
    }
}
