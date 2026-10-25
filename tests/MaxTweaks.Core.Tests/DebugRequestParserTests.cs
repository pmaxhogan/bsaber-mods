using System.Linq;
using MaxTweaks.Core.Debugging;
using Xunit;

namespace MaxTweaks.Core.Tests;

public class DebugRequestParserTests
{
    private static string[] Args(string text) => DebugCommandParser.Tokenize(text).ToArray();

    [Theory]
    [InlineData("expert", DebugDifficulty.Expert)]
    [InlineData("E+", DebugDifficulty.ExpertPlus)]
    [InlineData("ExpertPlus", DebugDifficulty.ExpertPlus)]
    [InlineData("easy", DebugDifficulty.Easy)]
    [InlineData("n", DebugDifficulty.Normal)]
    [InlineData("Hard", DebugDifficulty.Hard)]
    public void TryParseDifficulty_Names(string text, DebugDifficulty expected)
    {
        Assert.True(DebugRequestParser.TryParseDifficulty(text, out var d));
        Assert.Equal(expected, d);
    }

    [Fact]
    public void TryParsePlay_UnquotedMultiWordLevel()
    {
        Assert.True(DebugRequestParser.TryParsePlay(Args("849a (Sandstorm - Skyler Wallace) expert"), out var r, out _));
        Assert.Equal("849a (Sandstorm - Skyler Wallace)", r.Level);
        Assert.Equal(DebugDifficulty.Expert, r.Difficulty);
        Assert.Equal("Standard", r.Characteristic);
        Assert.False(r.Practice);
    }

    [Fact]
    public void TryParsePlay_CharacteristicAndPractice()
    {
        Assert.True(DebugRequestParser.TryParsePlay(Args("\"Sand storm\" hard OneSaber practice 1:20 0.8"), out var r, out _));
        Assert.Equal("Sand storm", r.Level);
        Assert.Equal(DebugDifficulty.Hard, r.Difficulty);
        Assert.Equal("OneSaber", r.Characteristic);
        Assert.True(r.Practice);
        Assert.Equal(80f, r.StartSeconds, 3);
        Assert.Equal(0.8f, r.Speed, 3);
    }

    [Fact]
    public void TryParsePlay_PracticeWithoutStart()
    {
        Assert.True(DebugRequestParser.TryParsePlay(Args("x expert practice"), out var r, out _));
        Assert.True(r.Practice);
        Assert.Equal(0f, r.StartSeconds);
        Assert.Equal(1f, r.Speed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("expert")]
    [InlineData("level")]
    [InlineData("level notadiff foo bar")]
    [InlineData("level expert practice abc")]
    [InlineData("level expert practice 10 5")]
    [InlineData("level expert practice 10 1 2")]
    [InlineData("\"\" expert")]
    public void TryParsePlay_Rejects(string text)
    {
        Assert.False(DebugRequestParser.TryParsePlay(Args(text), out _, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void TryParseButton_PressDefaultsAndDuration()
    {
        Assert.True(DebugRequestParser.TryParseButton(Args("y press"), out var r, out _));
        Assert.Equal(DebugButton.Y, r.Button);
        Assert.Equal(DebugButtonAction.Press, r.Action);
        Assert.Equal(DebugRequestParser.DefaultPressSeconds, r.Seconds);

        Assert.True(DebugRequestParser.TryParseButton(Args("A down 2.5"), out r, out _));
        Assert.Equal(DebugButtonAction.Down, r.Action);
        Assert.Equal(2.5f, r.Seconds);

        Assert.True(DebugRequestParser.TryParseButton(Args("b up"), out r, out _));
        Assert.Equal(DebugButtonAction.Up, r.Action);
        Assert.Equal(0f, r.Seconds);

        Assert.True(DebugRequestParser.TryParseButton(Args("x down"), out r, out _));
        Assert.Equal(DebugButton.X, r.Button);
    }

    [Fact]
    public void TryParseButton_Triggers()
    {
        Assert.True(DebugRequestParser.TryParseButton(Args("LT press"), out var r, out _));
        Assert.Equal(DebugButton.LeftTrigger, r.Button);
        Assert.Equal(DebugButtonAction.Press, r.Action);

        Assert.True(DebugRequestParser.TryParseButton(Args("rt down 0.5"), out r, out _));
        Assert.Equal(DebugButton.RightTrigger, r.Button);
        Assert.Equal(DebugButtonAction.Down, r.Action);
        Assert.Equal(0.5f, r.Seconds);
    }

    [Theory]
    [InlineData("z press")]
    [InlineData("trigger press")]
    [InlineData("a hold")]
    [InlineData("a")]
    [InlineData("a up 1")]
    [InlineData("a press -1")]
    [InlineData("a press 1 2")]
    public void TryParseButton_Rejects(string text)
    {
        Assert.False(DebugRequestParser.TryParseButton(Args(text), out _, out _));
    }

    [Fact]
    public void TryParseStick_ClampsAndReadsDuration()
    {
        Assert.True(DebugRequestParser.TryParseStick(Args("0 2 0.5"), out var r, out _));
        Assert.Equal(0f, r.X);
        Assert.Equal(1f, r.Y);
        Assert.Equal(0.5f, r.Seconds);

        Assert.True(DebugRequestParser.TryParseStick(Args("-3 0"), out r, out _));
        Assert.Equal(-1f, r.X);
        Assert.Equal(0f, r.Seconds);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("a b")]
    [InlineData("0 1 0")]
    [InlineData("0 1 2 3")]
    public void TryParseStick_Rejects(string text)
    {
        Assert.False(DebugRequestParser.TryParseStick(Args(text), out _, out _));
    }
}
