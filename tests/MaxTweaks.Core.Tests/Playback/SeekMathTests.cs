using MaxTweaks.Core.DifficultySwitch;
using MaxTweaks.Core.Playback;
using Xunit;

namespace MaxTweaks.Core.Tests.Playback;

public class SeekMathTests
{
    [Fact]
    public void LeadInLongerThanSpawnAhead_UsesNaturalPosition()
    {
        // Chapters: audio 1.6 s before the chapter, spawn-ahead 1.2 s.
        Assert.Equal(51.2f, SeekMath.CursorTarget(50f, 1.2f, 51.6f), 3);
    }

    [Fact]
    public void ObjectsFromBeforeNaturalPosition_CapsSoNothingIsSkipped()
    {
        // Short lead-in: objects from 50.5 must not be walked over.
        Assert.Equal(50.5f, SeekMath.CursorTarget(50f, 1.2f, 50.5f), 3);

        // Plain seek (objects from = audio): aheadTime > 0 entries stop at the audio time.
        Assert.Equal(50f, SeekMath.CursorTarget(50f, 1.2f, 50f), 3);
    }

    [Fact]
    public void ZeroAheadEntries_AlwaysAtAudioTime()
    {
        Assert.Equal(50f, SeekMath.CursorTarget(50f, 0f, 53f), 3);
        Assert.Equal(50f, SeekMath.CursorTarget(50f, 0f, 49f), 3);
        Assert.Equal(50f, SeekMath.CursorTarget(50f, -1f, 53f), 3);
    }

    [Fact]
    public void ObjectsFromBeforeAudio_NeverGoesBeforeAudio()
    {
        Assert.Equal(50f, SeekMath.CursorTarget(50f, 1.2f, 40f), 3);
    }

    [Theory]
    [InlineData(1f, 0.6f)]
    [InlineData(1.5f, 1.4f)]
    [InlineData(0.5f, 0.9f)]
    public void SwapWithClearGap_CursorAtNaturalPosition_FirstObjectFliesInFully(float timeScale, float spawnAhead)
    {
        const float Now = 40f;
        var objectsFrom = ClearGap.ObjectsFromTime(Now, 3f, spawnAhead, timeScale);
        var target = SeekMath.CursorTarget(Now, spawnAhead, objectsFrom);

        // The clear gap is always at least spawn-ahead + margin, so the cursor is not capped and the
        // first object that passes the filter is due after the cursor: it spawns at the far end.
        Assert.Equal(Now + spawnAhead, target, 3);
        Assert.True(objectsFrom >= target + ClearGap.SpawnMargin - 0.0001f);
    }
}
