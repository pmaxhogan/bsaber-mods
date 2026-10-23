using MaxTweaks.Core.Debugging;
using Xunit;

namespace MaxTweaks.Core.Tests;

public class DebugCommandParserTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("// comment")]
    [InlineData("; comment")]
    public void ParseLine_IgnoresBlankAndComments(string? line)
    {
        Assert.Null(DebugCommandParser.ParseLine(line));
    }

    [Fact]
    public void ParseLine_ReadsIdVerbAndQuotedArgs()
    {
        var cmd = DebugCommandParser.ParseLine((char)0xFEFF + "#a1 PLAY \"849a (Sandstorm - X)\" expert practice 20")!;
        Assert.Equal("a1", cmd.Id);
        Assert.Equal("play", cmd.Verb);
        Assert.Equal(new[] { "849a (Sandstorm - X)", "expert", "practice", "20" }, cmd.Args);
    }

    [Fact]
    public void ParseLine_WithoutId()
    {
        var cmd = DebugCommandParser.ParseLine("log hello world")!;
        Assert.Null(cmd.Id);
        Assert.Equal("log", cmd.Verb);
        Assert.Equal(2, cmd.Args.Count);
    }

    [Fact]
    public void ParseLine_IdOnly_HasEmptyVerb()
    {
        var cmd = DebugCommandParser.ParseLine("#7")!;
        Assert.Equal("7", cmd.Id);
        Assert.Equal(string.Empty, cmd.Verb);
        Assert.Empty(cmd.Args);
    }

    [Fact]
    public void Tokenize_HandlesEscapedQuotesAndEmptyQuotes()
    {
        Assert.Equal(new[] { "a \"b\"", "" }, DebugCommandParser.Tokenize("\"a \\\"b\\\"\" \"\""));
    }

    [Theory]
    [InlineData("20", 20f)]
    [InlineData("1:05.5", 65.5f)]
    [InlineData("0:00", 0f)]
    public void TryParseSeconds_Accepts(string text, float expected)
    {
        Assert.True(DebugCommandParser.TryParseSeconds(text, out var s));
        Assert.Equal(expected, s, 3);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("1:75")]
    [InlineData("x:10")]
    [InlineData("abc")]
    public void TryParseSeconds_Rejects(string text)
    {
        Assert.False(DebugCommandParser.TryParseSeconds(text, out _));
    }
}
