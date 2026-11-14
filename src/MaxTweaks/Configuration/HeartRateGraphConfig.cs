namespace MaxTweaks.Configuration;

internal class HeartRateGraphConfig
{
    /// <summary>On by default: nothing is drawn until a heart rate arrives (see the heart rate bridge).</summary>
    public virtual bool Enabled { get; set; } = true;

    /// <summary>Heart rate on the bottom edge of the graph.</summary>
    public virtual int MinBpm { get; set; } = 100;

    /// <summary>Heart rate on the top edge of the graph.</summary>
    public virtual int MaxBpm { get; set; } = 200;

    /// <summary>Graph width in metres: the whole song, start to end.</summary>
    public virtual float Width { get; set; } = 2.1f;

    /// <summary>Graph height in metres: MinBpm to MaxBpm.</summary>
    public virtual float Height { get; set; } = 0.7f;

    /// <summary>Space between the lowest point of HRCounter's counter and the top of the graph, in metres.</summary>
    public virtual float GapBelowHRCounter { get; set; } = 0.05f;

    /// <summary>Centre of the graph when HRCounter's counter is not on screen (metres; HRCounter's default spot is 0, 1.2, 7).</summary>
    public virtual float FallbackX { get; set; } = 0f;

    public virtual float FallbackY { get; set; } = 0.8f;

    public virtual float FallbackZ { get; set; } = 7f;
}
