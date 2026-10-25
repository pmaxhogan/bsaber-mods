using System;

namespace MaxTweaks.Core.HeartRateBridge;

/// <summary>
/// Decides how long to wait before starting the bridge process again after it exited. Crashes back
/// off exponentially (5 s doubling to 60 s) and the backoff resets once a run lasted a while; a run
/// that quit because another bridge is already running is retried slowly, so a bridge started by
/// hand keeps working and is taken over when it stops.
/// </summary>
public sealed class BridgeRestartPolicy
{
    public const double InitialDelaySeconds = 5;
    public const double MaxDelaySeconds = 60;
    public const double HealthyRunSeconds = 60;
    public const double AlreadyRunningDelaySeconds = 30;

    private double _nextDelay = InitialDelaySeconds;

    /// <summary>Delay in seconds before the next start, given how the last run ended.</summary>
    public double DelayAfterExit(int exitCode, double ranSeconds)
    {
        if (exitCode == BridgeCommandLine.AlreadyRunningExitCode)
        {
            return AlreadyRunningDelaySeconds;
        }

        if (ranSeconds >= HealthyRunSeconds)
        {
            _nextDelay = InitialDelaySeconds;
        }

        var delay = _nextDelay;
        _nextDelay = Math.Min(MaxDelaySeconds, _nextDelay * 2);
        return delay;
    }

    public void Reset() => _nextDelay = InitialDelaySeconds;
}
