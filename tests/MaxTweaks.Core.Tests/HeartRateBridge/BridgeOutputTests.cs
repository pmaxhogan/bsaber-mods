using MaxTweaks.Core.HeartRateBridge;
using Xunit;

namespace MaxTweaks.Core.Tests.HeartRateBridge;

public class BridgeOutputTests
{
    [Theory]
    [InlineData("13:05:02 80 bpm (sent 1)", BridgeEvent.Reading, 80)]
    [InlineData("13:05:01 connected, bridging (Ctrl+C to stop)", BridgeEvent.Connected, 0)]
    [InlineData("13:07:00 strap disconnected", BridgeEvent.Disconnected, 0)]
    [InlineData("13:04:48 scanning 6s for heart-rate straps (wear it, electrodes wet)...", BridgeEvent.Scanning, 0)]
    [InlineData("13:04:55 connection failed: timeout", BridgeEvent.Error, 0)]
    [InlineData("Traceback (most recent call last):", BridgeEvent.Error, 0)]
    [InlineData("ModuleNotFoundError: No module named 'bleak'", BridgeEvent.Error, 0)]
    [InlineData("13:04:54   DD:BF:63:2C:CA:12  TICKR C8D8  rssi -63", BridgeEvent.Other, 0)]
    [InlineData("", BridgeEvent.Other, 0)]
    public void Classify(string line, BridgeEvent expected, int expectedBpm)
    {
        Assert.Equal(expected, BridgeOutput.Classify(line, out var bpm));
        Assert.Equal(expectedBpm, bpm);
    }

    [Fact]
    public void Strip_RemovesTimestampOnly()
    {
        Assert.Equal("80 bpm (sent 1)", BridgeOutput.Strip("13:05:02 80 bpm (sent 1)  "));
        Assert.Equal("no stamp here", BridgeOutput.Strip("no stamp here"));
        Assert.Equal("", BridgeOutput.Strip(null));
    }
}
