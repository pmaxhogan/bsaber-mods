using System;
using UnityEngine;

namespace MaxTweaks.Common.HeartRate;

/// <summary>
/// The live heart rate, shared between features (bound once in the App container). The heart rate
/// bridge publishes every reading of the strap here; the heart rate graph listens. Main thread only.
///
///   [Inject] HeartRateFeed feed;
///   feed.Reading += bpm => ...;                 // every reading, about once a second
///   if (feed.TryGetFresh(3.0, out var bpm)) ... // the last reading, if it is recent enough
/// </summary>
internal sealed class HeartRateFeed
{
    private const string Feature = "HeartRate";

    /// <summary>Raised on the main thread for every reading, in beats per minute.</summary>
    public event Action<int>? Reading;

    /// <summary>The last reading (0 before the first one).</summary>
    public int Bpm { get; private set; }

    /// <summary>Time.realtimeSinceStartupAsDouble of the last reading.</summary>
    public double LastReadingRealtime { get; private set; } = double.NegativeInfinity;

    public void Publish(int bpm)
    {
        if (bpm <= 0)
        {
            return;
        }

        Bpm = bpm;
        LastReadingRealtime = Time.realtimeSinceStartupAsDouble;
        if (Reading == null)
        {
            return;
        }

        foreach (var handler in Reading.GetInvocationList())
        {
            Guard.Run(Feature, () => ((Action<int>)handler)(bpm));
        }
    }

    /// <summary>The last reading, unless it is older than <paramref name="maxAgeSeconds"/> (strap off or dropped).</summary>
    public bool TryGetFresh(double maxAgeSeconds, out int bpm)
    {
        bpm = Bpm;
        return bpm > 0 && Time.realtimeSinceStartupAsDouble - LastReadingRealtime <= maxAgeSeconds;
    }
}
