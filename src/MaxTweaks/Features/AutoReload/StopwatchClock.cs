using System.Diagnostics;
using MaxTweaks.Core.AutoReload;

namespace MaxTweaks.Features.AutoReload;

/// <summary>Monotonic seconds, readable from any thread (unlike UnityEngine.Time).</summary>
internal sealed class StopwatchClock : IClock
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    public double Now => _stopwatch.Elapsed.TotalSeconds;
}
