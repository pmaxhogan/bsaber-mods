using MaxTweaks.Core.ObsStatus;
using Xunit;

namespace MaxTweaks.Core.Tests.ObsStatus;

public class ObsStatusLineTests
{
    private const string Connected = "OBS <color=#70e070>connected</color>";
    private const string NotConnected = "OBS <color=#ff7070>not connected</color>";
    private const string Connecting = "OBS <color=#ffc050>connecting</color>";

    private static readonly ObsFeatureState Off = default;

    [Fact]
    public void BothOff_NothingToShow()
    {
        Assert.Null(ObsStatusLine.Build(Off, Off));

        // Counts alone do not bring the line back: with both features off nobody talks to OBS.
        Assert.Null(ObsStatusLine.Build(new ObsFeatureState(false, false, 3), new ObsFeatureState(false, true, 5)));
    }

    [Fact]
    public void BothConnected_ShowsBothCounts()
    {
        var line = ObsStatusLine.Build(new ObsFeatureState(true, true, 3), new ObsFeatureState(true, true, 12));

        Assert.Equal(Connected + "   |   3 recordings   |   12 clips", line);
    }

    [Fact]
    public void Counts_AreSingularForOne_AndShownAtZero()
    {
        Assert.Equal(
            Connected + "   |   1 recording   |   1 clip",
            ObsStatusLine.Build(new ObsFeatureState(true, true, 1), new ObsFeatureState(true, true, 1)));
        Assert.Equal(
            Connected + "   |   0 recordings   |   0 clips",
            ObsStatusLine.Build(new ObsFeatureState(true, true, 0), new ObsFeatureState(true, true, 0)));
    }

    [Fact]
    public void NotConnected_KeepsTheCounts()
    {
        var line = ObsStatusLine.Build(new ObsFeatureState(true, false, 2), new ObsFeatureState(true, false, 7));

        Assert.Equal(NotConnected + "   |   2 recordings   |   7 clips", line);
    }

    [Fact]
    public void OneOfTwoConnected_IsConnecting()
    {
        Assert.StartsWith(Connecting, ObsStatusLine.Build(new ObsFeatureState(true, true, 0), new ObsFeatureState(true, false, 0)));
        Assert.StartsWith(Connecting, ObsStatusLine.Build(new ObsFeatureState(true, false, 0), new ObsFeatureState(true, true, 0)));
    }

    [Fact]
    public void OnlyTheEnabledFeatureDecidesAndCounts()
    {
        Assert.Equal(Connected + "   |   4 recordings", ObsStatusLine.Build(new ObsFeatureState(true, true, 4), Off));
        Assert.Equal(NotConnected + "   |   0 clips", ObsStatusLine.Build(Off, new ObsFeatureState(true, false, 0)));

        // A recording feature that is off but still holds its connection (a recording being finished) does not count.
        Assert.Equal(NotConnected + "   |   0 clips", ObsStatusLine.Build(new ObsFeatureState(false, true, 0), new ObsFeatureState(true, false, 0)));
    }

    [Fact]
    public void SwitchedOffFeature_KeepsItsCount_WhenItSavedSomething()
    {
        var line = ObsStatusLine.Build(new ObsFeatureState(true, true, 4), new ObsFeatureState(false, false, 9));

        Assert.Equal(Connected + "   |   4 recordings   |   9 clips", line);
    }

    [Fact]
    public void Trouble_TakesThePlaceOfConnected_OnlyWhileAllAreConnected()
    {
        var trouble = new ObsFeatureState(true, true, 2, "replay buffer off");
        Assert.Equal(
            "OBS <color=#ffc050>replay buffer off</color>   |   0 recordings   |   2 clips",
            ObsStatusLine.Build(new ObsFeatureState(true, true, 0), trouble));
        Assert.Equal(
            "OBS <color=#ffc050>disk full</color>   |   0 recordings   |   2 clips",
            ObsStatusLine.Build(new ObsFeatureState(true, true, 0, "disk full"), trouble));

        // Not connected says more than a stale trouble; so does a feature that is still connecting.
        var stale = new ObsFeatureState(true, false, 2, "replay buffer off");
        Assert.Equal(NotConnected + "   |   2 clips", ObsStatusLine.Build(Off, stale));
        Assert.StartsWith(Connecting, ObsStatusLine.Build(new ObsFeatureState(true, false, 0), trouble));

        // No words, no trouble; and a feature that is off has none.
        Assert.Equal(Connected + "   |   2 clips", ObsStatusLine.Build(Off, new ObsFeatureState(true, true, 2, string.Empty)));
        Assert.Equal(Connected + "   |   1 recording   |   2 clips", ObsStatusLine.Build(new ObsFeatureState(true, true, 1), new ObsFeatureState(false, true, 2, "replay buffer off")));
    }

    [Fact]
    public void Trouble_CannotSmuggleInTags()
    {
        var line = ObsStatusLine.Build(Off, new ObsFeatureState(true, true, 0, "<size=900>big</size>"));

        Assert.Equal("OBS <color=#ffc050>(size=900)big(/size)</color>   |   0 clips", line);
    }

    [Fact]
    public void State_ComparesByValue()
    {
        var a = new ObsFeatureState(true, true, 3, "x");

        Assert.True(a == new ObsFeatureState(true, true, 3, "x"));
        Assert.True(a.Equals((object)new ObsFeatureState(true, true, 3, "x")));
        Assert.Equal(a.GetHashCode(), new ObsFeatureState(true, true, 3, "x").GetHashCode());
        Assert.True(a != new ObsFeatureState(false, true, 3, "x"));
        Assert.True(a != new ObsFeatureState(true, false, 3, "x"));
        Assert.True(a != new ObsFeatureState(true, true, 4, "x"));
        Assert.True(a != new ObsFeatureState(true, true, 3));
        Assert.False(a.Equals("x"));
        Assert.Equal(default(ObsFeatureState).GetHashCode(), Off.GetHashCode());
    }
}
