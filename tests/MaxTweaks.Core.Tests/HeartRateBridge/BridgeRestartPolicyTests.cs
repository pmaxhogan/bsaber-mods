using MaxTweaks.Core.HeartRateBridge;
using Xunit;

namespace MaxTweaks.Core.Tests.HeartRateBridge;

public class BridgeRestartPolicyTests
{
    [Fact]
    public void Crashes_BackOffExponentially_UpToMax()
    {
        var policy = new BridgeRestartPolicy();
        Assert.Equal(5, policy.DelayAfterExit(1, 2));
        Assert.Equal(10, policy.DelayAfterExit(1, 2));
        Assert.Equal(20, policy.DelayAfterExit(1, 2));
        Assert.Equal(40, policy.DelayAfterExit(1, 2));
        Assert.Equal(60, policy.DelayAfterExit(1, 2));
        Assert.Equal(60, policy.DelayAfterExit(1, 2));
    }

    [Fact]
    public void HealthyRun_ResetsBackoff()
    {
        var policy = new BridgeRestartPolicy();
        policy.DelayAfterExit(1, 2);
        policy.DelayAfterExit(1, 2);
        Assert.Equal(5, policy.DelayAfterExit(0, 600));
        Assert.Equal(10, policy.DelayAfterExit(0, 1));
    }

    [Fact]
    public void AlreadyRunning_IsRetriedSlowly_WithoutGrowing()
    {
        var policy = new BridgeRestartPolicy();
        Assert.Equal(30, policy.DelayAfterExit(BridgeCommandLine.AlreadyRunningExitCode, 1));
        Assert.Equal(30, policy.DelayAfterExit(BridgeCommandLine.AlreadyRunningExitCode, 1));
        Assert.Equal(5, policy.DelayAfterExit(1, 1));
    }

    [Fact]
    public void Reset_StartsOver()
    {
        var policy = new BridgeRestartPolicy();
        policy.DelayAfterExit(1, 1);
        policy.Reset();
        Assert.Equal(5, policy.DelayAfterExit(1, 1));
    }
}
