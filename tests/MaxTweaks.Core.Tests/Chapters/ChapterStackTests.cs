using System;
using System.Linq;
using MaxTweaks.Core.Chapters;
using Xunit;

namespace MaxTweaks.Core.Tests.Chapters;

public class ChapterStackTests
{
    private static readonly Chapter Song = new(0, "song", 0f, 100f);
    private static readonly Chapter Verse = new(1, "verse", 10f, 40f);
    private static readonly Chapter Hook = new(2, "hook", 20f, 30f);
    private static readonly Chapter Outro = new(3, "outro", 40f, 70f);

    [Fact]
    public void Order_IsLongestFirst_ThenStart_ThenFileOrder()
    {
        var a = new Chapter(5, "a", 50f, 60f);
        var b = new Chapter(1, "b", 10f, 20f);
        var c = new Chapter(0, "c", 10f, 20f);
        var stack = new ChapterStack(new[] { Hook, a, Song, b, c });
        Assert.Equal(new[] { "song", "c", "b", "hook", "a" }, stack.Chapters.Select(x => x.Label));
        Assert.Equal(5, stack.Count);
    }

    [Fact]
    public void GetActive_ReturnsBottomFirst()
    {
        var stack = new ChapterStack(new[] { Hook, Outro, Song, Verse });
        var buffer = new int[4];

        var count = stack.GetActive(25f, buffer);
        Assert.Equal(new[] { "song", "verse", "hook" }, buffer.Take(count).Select(i => stack.Chapters[i].Label));

        // Back-to-back chapters: at 40 the verse has ended and the outro has begun.
        count = stack.GetActive(40f, buffer);
        Assert.Equal(new[] { "song", "outro" }, buffer.Take(count).Select(i => stack.Chapters[i].Label));

        Assert.Equal(0, stack.GetActive(100f, buffer));
        Assert.Equal(0, stack.GetActive(-1f, buffer));
    }

    [Fact]
    public void GetActive_DropsLongestWhenBufferTooSmall()
    {
        var stack = new ChapterStack(new[] { Song, Verse, Hook });
        var buffer = new int[2];
        var count = stack.GetActive(25f, buffer);
        Assert.Equal(2, count);
        Assert.Equal(new[] { "verse", "hook" }, buffer.Take(count).Select(i => stack.Chapters[i].Label));
    }

    [Fact]
    public void GetActive_NullBuffer_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ChapterStack(new[] { Song }).GetActive(1f, null!));
    }

    [Fact]
    public void MaxConcurrent_CountsOverlapsOnly()
    {
        Assert.Equal(0, new ChapterStack(Array.Empty<Chapter>()).MaxConcurrent);
        Assert.Equal(1, new ChapterStack(new[] { Verse, Outro }).MaxConcurrent);
        Assert.Equal(3, new ChapterStack(new[] { Song, Verse, Hook, Outro }).MaxConcurrent);
        Assert.Equal(2, new ChapterStack(new[] { Song, Song }).MaxConcurrent);
    }
}
