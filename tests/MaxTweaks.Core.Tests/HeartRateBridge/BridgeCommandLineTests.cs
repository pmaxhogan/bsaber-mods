using MaxTweaks.Core.HeartRateBridge;
using Xunit;

namespace MaxTweaks.Core.Tests.HeartRateBridge;

public class BridgeCommandLineTests
{
    [Fact]
    public void Arguments_QuotesScriptPathWithSpaces_AndAppendsExtras()
    {
        var args = BridgeCommandLine.Arguments(@"C:\Beat Saber\UserData\MaxTweaks\hr_osc_bridge.py", "  --name TICKR ", 4242);
        Assert.Equal(@"run --python 3.13 ""C:\Beat Saber\UserData\MaxTweaks\hr_osc_bridge.py"" --parent-pid 4242 --name TICKR", args);
    }

    [Fact]
    public void Arguments_NoExtras_NoTrailingSpace()
    {
        Assert.Equal(@"run --python 3.13 C:\bs\hr.py --parent-pid 1", BridgeCommandLine.Arguments(@"C:\bs\hr.py", "", 1));
        Assert.Equal(@"run --python 3.13 C:\bs\hr.py --parent-pid 1", BridgeCommandLine.Arguments(@"C:\bs\hr.py", null, 1));
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("", "\"\"")]
    [InlineData("has space", "\"has space\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData("C:\\dir with space\\", "\"C:\\dir with space\\\\\"")]
    [InlineData("back\\\"slash", "\"back\\\\\\\"slash\"")]
    public void Quote_FollowsWindowsRules(string input, string expected)
    {
        Assert.Equal(expected, BridgeCommandLine.Quote(input));
    }
}
