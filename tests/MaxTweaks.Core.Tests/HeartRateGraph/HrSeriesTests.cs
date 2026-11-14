using System.Linq;
using MaxTweaks.Core.HeartRateGraph;
using Xunit;

namespace MaxTweaks.Core.Tests.HeartRateGraph;

public class HrSeriesTests
{
    private static (double Time, int Bpm, bool StartsRun)[] Dump(HrSeries series) =>
        series.Samples.Select(s => (s.Time, s.Bpm, s.StartsRun)).ToArray();

    [Fact]
    public void SteadyReadings_FormOneRun()
    {
        var series = new HrSeries();
        series.Add(0, 120);
        series.Add(1, 122);
        series.Add(2, 125);

        Assert.Equal(new[] { (0.0, 120, true), (1.0, 122, false), (2.0, 125, false) }, Dump(series));
        Assert.Equal(3, series.Version);
    }

    [Fact]
    public void LongGap_StartsANewRun()
    {
        var series = new HrSeries(maxGapSeconds: 5);
        series.Add(10, 130);
        series.Add(15, 131);
        series.Add(20.1, 140);

        Assert.Equal(new[] { (10.0, 130, true), (15.0, 131, false), (20.1, 140, true) }, Dump(series));
    }

    [Fact]
    public void ReadingsWhileTimeStandsStill_ReplaceTheLastSample()
    {
        var series = new HrSeries(minStepSeconds: 0.25);
        series.Add(30, 150);
        series.Add(31, 152);
        var version = series.Version;

        series.Add(31.1, 148);
        series.Add(30.9, 141);
        Assert.Equal(new[] { (30.0, 150, true), (31.0, 141, false) }, Dump(series));
        Assert.Equal(version + 2, series.Version);

        series.Add(31.0, 141);
        Assert.Equal(version + 2, series.Version);
    }

    [Fact]
    public void JumpBack_DropsEverythingFromTheNewTimeOn()
    {
        var series = new HrSeries();
        for (var t = 0; t <= 10; t++)
        {
            series.Add(t, 100 + t);
        }

        series.Add(4.5, 160);

        Assert.Equal(
            new[] { (0.0, 100, true), (1.0, 101, false), (2.0, 102, false), (3.0, 103, false), (4.0, 104, false), (4.5, 160, false) },
            Dump(series));
    }

    [Fact]
    public void JumpBackBeforeTheFirstSample_StartsOver()
    {
        var series = new HrSeries();
        series.Add(60, 170);
        series.Add(61, 171);

        series.Add(5, 120);

        Assert.Equal(new[] { (5.0, 120, true) }, Dump(series));
    }

    [Fact]
    public void JumpBackFarBehindTheKeptSamples_LeavesAHole()
    {
        var series = new HrSeries(maxGapSeconds: 5);
        series.Add(0, 110);
        series.Add(1, 111);
        series.Add(40, 150);
        series.Add(41, 151);

        series.Add(20, 130);

        Assert.Equal(new[] { (0.0, 110, true), (1.0, 111, false), (20.0, 130, true) }, Dump(series));
    }

    [Theory]
    [InlineData(double.NaN, 120)]
    [InlineData(double.PositiveInfinity, 120)]
    [InlineData(5.0, 0)]
    [InlineData(5.0, -3)]
    public void BadReadings_AreIgnored(double time, int bpm)
    {
        var series = new HrSeries();
        series.Add(time, bpm);
        Assert.Empty(series.Samples);
        Assert.Equal(0, series.Version);
    }

    [Fact]
    public void Clear_EmptiesAndBumpsVersionOnce()
    {
        var series = new HrSeries();
        series.Clear();
        Assert.Equal(0, series.Version);

        series.Add(1, 120);
        series.Clear();
        Assert.Empty(series.Samples);
        Assert.Equal(2, series.Version);
    }
}
