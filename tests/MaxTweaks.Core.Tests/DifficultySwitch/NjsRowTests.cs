using System.Linq;
using MaxTweaks.Core.DifficultySwitch;
using Xunit;

namespace MaxTweaks.Core.Tests.DifficultySwitch;

public class NjsRowTests
{
    [Fact]
    public void IntegerDefaultInRange_IsNotDuplicated()
    {
        var row = NjsRow.Build(16f, 16f);
        Assert.Equal(Enumerable.Range(8, 23).Select(i => (float)i), row.Values);
        Assert.Equal(8, row.DefaultIndex);
        Assert.Equal(8, row.CurrentIndex);
        Assert.Equal(16f, row.Default);
    }

    [Fact]
    public void FractionalDefault_InsertedAtSortedPlace()
    {
        var row = NjsRow.Build(16.5f, 16.5f);
        Assert.Equal(24, row.Count);
        Assert.Equal(16f, row.Values[8]);
        Assert.Equal(16.5f, row.Values[9]);
        Assert.Equal(17f, row.Values[10]);
        Assert.Equal(9, row.DefaultIndex);
        Assert.Equal(9, row.CurrentIndex);
    }

    [Fact]
    public void DefaultBelowRange_InsertedFirst()
    {
        var row = NjsRow.Build(5f, 5f);
        Assert.Equal(5f, row.Values[0]);
        Assert.Equal(8f, row.Values[1]);
        Assert.Equal(0, row.DefaultIndex);
    }

    [Fact]
    public void DefaultAboveRange_InsertedLast()
    {
        var row = NjsRow.Build(32f, 32f);
        Assert.Equal(32f, row.Values[row.Count - 1]);
        Assert.Equal(30f, row.Values[row.Count - 2]);
        Assert.Equal(row.Count - 1, row.DefaultIndex);
    }

    [Fact]
    public void CurrentDiffersFromDefault_HighlightStartsOnCurrent()
    {
        var row = NjsRow.Build(16f, 20f);
        Assert.Equal(8, row.DefaultIndex);
        Assert.Equal(12, row.CurrentIndex);
        Assert.Equal(20f, row.Current);
    }

    [Fact]
    public void CurrentNotInRow_IsInsertedToo()
    {
        var row = NjsRow.Build(16f, 17.25f);
        Assert.Equal(24, row.Count);
        Assert.Equal(17.25f, row.Values[row.CurrentIndex]);
        Assert.Equal(16f, row.Values[row.DefaultIndex]);
    }

    [Fact]
    public void NearlyEqualValues_AreTheSameEntry()
    {
        var row = NjsRow.Build(16.0004f, 16f);
        Assert.Equal(23, row.Count);
        Assert.Equal(row.DefaultIndex, row.CurrentIndex);
        Assert.Equal(16f, row.Values[row.DefaultIndex]);
    }

    [Fact]
    public void InvalidDefault_RowIsEmpty()
    {
        Assert.Equal(0, NjsRow.Build(float.NaN, 16f).Count);
        Assert.Equal(0, NjsRow.Build(0f, 16f).Count);
        Assert.Equal(0, NjsRow.Build(float.PositiveInfinity, 16f).Count);
        Assert.Equal(-1, NjsRow.Build(-3f, 16f).DefaultIndex);
    }

    [Fact]
    public void InvalidCurrent_FallsBackToDefault()
    {
        var row = NjsRow.Build(16f, float.NaN);
        Assert.Equal(row.DefaultIndex, row.CurrentIndex);
        Assert.Equal(23, row.Count);
    }

    [Fact]
    public void IndexOf_UsesTolerance()
    {
        var row = NjsRow.Build(16f, 16f);
        Assert.Equal(10, row.IndexOf(18.0005f));
        Assert.Equal(-1, row.IndexOf(18.5f));
        Assert.Equal(-1, row.IndexOf(float.NaN));
    }

    [Theory]
    [InlineData(0, 23, 9, 0)]
    [InlineData(4, 23, 9, 0)]
    [InlineData(5, 23, 9, 1)]
    [InlineData(11, 23, 9, 7)]
    [InlineData(22, 23, 9, 14)]
    [InlineData(2, 5, 9, 0)]
    [InlineData(0, 0, 9, 0)]
    public void WindowStart_CentresAndClamps(int highlight, int count, int width, int expected)
    {
        Assert.Equal(expected, NjsRow.WindowStart(highlight, count, width));
    }

    [Fact]
    public void Formatting_IsInvariant()
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal("16.5", NjsRow.Label(16.5f));
            Assert.Equal("18", NjsRow.Label(18f));
            Assert.Equal("17.33", NjsRow.Label(17.333f));
            Assert.Equal("NJS 18 (default 16.5)", NjsRow.Toast(18f, 16.5f));
            Assert.Equal("njs 16 -> 18 (default 16)", NjsRow.LogChange(16f, 18f, 16f));
            Assert.Equal("njs=18/16", NjsRow.Status(18f, 16f));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
    }
}
