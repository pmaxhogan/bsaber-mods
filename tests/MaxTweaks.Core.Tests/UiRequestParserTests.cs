using System.Linq;
using MaxTweaks.Core.Debugging;
using Xunit;

namespace MaxTweaks.Core.Tests;

public class UiRequestParserTests
{
    private static string[] Args(string text) => DebugCommandParser.Tokenize(text).ToArray();

    [Theory]
    [InlineData("find-hint", UiMatchKind.Hint, false)]
    [InlineData("find-text", UiMatchKind.Text, false)]
    [InlineData("find-name", UiMatchKind.Name, false)]
    [InlineData("click-hint", UiMatchKind.Hint, true)]
    [InlineData("click-text", UiMatchKind.Text, true)]
    [InlineData("click-name", UiMatchKind.Name, true)]
    public void TryParseQuery_AllVerbs(string verb, UiMatchKind kind, bool click)
    {
        Assert.Contains(verb, UiRequestParser.QueryVerbs);
        Assert.True(UiRequestParser.TryParseQuery(verb, Args("\"Hide OST\""), out var r, out _));
        Assert.Equal(kind, r.Kind);
        Assert.Equal(click, r.Click);
        Assert.Equal("Hide OST", r.Needle);
        Assert.Equal(0, r.Index);
    }

    [Fact]
    public void TryParseQuery_ClickIndex()
    {
        Assert.True(UiRequestParser.TryParseQuery("click-text", Args("Play 2"), out var r, out _));
        Assert.Equal(2, r.Index);
    }

    [Theory]
    [InlineData("click-text", "")]
    [InlineData("click-text", "\"\"")]
    [InlineData("click-text", "Play x")]
    [InlineData("click-text", "Play -1")]
    [InlineData("click-text", "Play 1 2")]
    [InlineData("find-text", "Play 1")]
    [InlineData("click-color", "Play")]
    [InlineData("tap-text", "Play")]
    [InlineData("click", "Play")]
    public void TryParseQuery_Rejects(string verb, string args)
    {
        Assert.False(UiRequestParser.TryParseQuery(verb, Args(args), out _, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void TryParsePatches()
    {
        Assert.True(UiRequestParser.TryParsePatches(Args("LevelCollectionTableView SetData"), out var type, out var method, out _));
        Assert.Equal("LevelCollectionTableView", type);
        Assert.Equal("SetData", method);
        Assert.False(UiRequestParser.TryParsePatches(Args("OnlyType"), out _, out _, out var error));
        Assert.NotEmpty(error);
        Assert.False(UiRequestParser.TryParsePatches(Args("\"\" m"), out _, out _, out _));
    }

    [Fact]
    public void TryParseSingle()
    {
        Assert.True(UiRequestParser.TryParseSingle("textinfo", Args("\"Hide OSTs\""), out var value, out _));
        Assert.Equal("Hide OSTs", value);
        Assert.False(UiRequestParser.TryParseSingle("textinfo", Args(""), out _, out var error));
        Assert.Equal("usage: textinfo <text>", error);
        Assert.False(UiRequestParser.TryParseSingle("textinfo", Args("a b"), out _, out _));
    }
}
