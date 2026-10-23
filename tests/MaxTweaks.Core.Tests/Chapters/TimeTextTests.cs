using MaxTweaks.Core.Chapters;
using Xunit;

namespace MaxTweaks.Core.Tests.Chapters;

public class TimeTextTests
{
    [Theory]
    [InlineData("0", 0f)]
    [InlineData("12", 12f)]
    [InlineData("12.25", 12.25f)]
    [InlineData("1:05", 65f)]
    [InlineData("1:05.5", 65.5f)]
    [InlineData("0:00.125", 0.125f)]
    [InlineData("10:00", 600f)]
    [InlineData("75:30", 4530f)]
    [InlineData("1:02:03", 3723f)]
    [InlineData("1:02:03.5", 3723.5f)]
    [InlineData("  2:00  ", 120f)]
    public void TryParse_Accepts(string text, float expected)
    {
        Assert.True(TimeText.TryParse(text, out var seconds));
        Assert.Equal(expected, seconds, 3);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-5")]
    [InlineData("+5")]
    [InlineData("1:60")]
    [InlineData("1:2:60")]
    [InlineData("1:-5")]
    [InlineData("1.5:00")]
    [InlineData("1::00")]
    [InlineData(":30")]
    [InlineData("1:")]
    [InlineData("1:2:3:4")]
    [InlineData("abc")]
    [InlineData("1e3")]
    [InlineData("1,5")]
    public void TryParse_Rejects(string? text)
    {
        Assert.False(TimeText.TryParse(text, out _));
    }

    [Theory]
    [InlineData(0f, "0:00.000")]
    [InlineData(65.5f, "1:05.500")]
    [InlineData(599.9996f, "10:00.000")]
    [InlineData(3723.25f, "62:03.250")]
    public void Format_RoundTrips(float seconds, string expected)
    {
        Assert.Equal(expected, TimeText.Format(seconds));
        Assert.True(TimeText.TryParse(expected, out var back));
        Assert.Equal(seconds, back, 2);
    }

    [Fact]
    public void Format_PassesThroughOddValues()
    {
        Assert.Equal("-1", TimeText.Format(-1f));
        Assert.Equal("NaN", TimeText.Format(float.NaN));
    }
}
