using System.Diagnostics;
using MaxTweaks.Core.AutoReload;

namespace MaxTweaks.Common;

/// <summary>
/// Shared between a feature that writes sidecar files next to a map (Active pause flags) and Auto
/// reload, which must not treat those writes as a map change (see <see cref="SidecarWriteGuard"/>).
/// </summary>
internal static class SidecarWrites
{
    public static SidecarWriteGuard Guard { get; } = new(new MonotonicClock());

    private sealed class MonotonicClock : IClock
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        public double Now => _stopwatch.Elapsed.TotalSeconds;
    }
}
