using System;
using System.Collections.Generic;
using MaxTweaks.Core.AutoReload;
using Xunit;

namespace MaxTweaks.Core.Tests.AutoReload;

public class ClassificationAndSummaryTests
{
    [Theory]
    [InlineData(MapFolderState.Missing, true, MapChangeKind.Removed)]
    [InlineData(MapFolderState.Missing, false, MapChangeKind.None)]
    [InlineData(MapFolderState.NoInfoDat, true, MapChangeKind.AwaitingInfoDat)]
    [InlineData(MapFolderState.NoInfoDat, false, MapChangeKind.AwaitingInfoDat)]
    [InlineData(MapFolderState.Ready, true, MapChangeKind.Updated)]
    [InlineData(MapFolderState.Ready, false, MapChangeKind.Added)]
    public void Classify(MapFolderState state, bool loaded, MapChangeKind expected)
    {
        Assert.Equal(expected, MapChangeClassifier.Classify(state, loaded));
    }

    [Fact]
    public void Summary_CountsAddedUpdatedRemoved()
    {
        var before = new[] { @"C:\L\Kept", @"C:\L\Edited", @"C:\L\Gone" };
        var after = new[] { @"C:\L\Kept", @"C:\L\Edited", @"C:\L\New1", @"C:\L\New2" };
        var summary = ReloadSummary.Compute(before, after, new[] { @"C:\L\Edited" }, new[] { @"C:\L\New1", @"C:\L\New2" });
        Assert.Equal(2, summary.Added);
        Assert.Equal(1, summary.Updated);
        Assert.Equal(1, summary.Removed);
        Assert.Equal(0, summary.Failed);
        Assert.Equal("2 maps added, 1 updated, 1 removed", summary.ToToastText());
    }

    [Fact]
    public void Summary_IsCaseInsensitive()
    {
        var summary = ReloadSummary.Compute(new[] { @"C:\L\Map" }, new[] { @"c:\l\MAP" }, new[] { @"C:\L\map" }, Array.Empty<string>());
        Assert.Equal(0, summary.Added);
        Assert.Equal(1, summary.Updated);
        Assert.Equal(0, summary.Removed);
    }

    [Fact]
    public void Summary_UpdatedMapThatFailsToReload_IsFailedNotRemoved()
    {
        var summary = ReloadSummary.Compute(new[] { @"C:\L\Broken" }, Array.Empty<string>(), new[] { @"C:\L\Broken" }, Array.Empty<string>());
        Assert.Equal(0, summary.Removed);
        Assert.Equal(0, summary.Updated);
        Assert.Equal(1, summary.Failed);
        Assert.Equal("1 map failed to load", summary.ToToastText());
    }

    [Fact]
    public void Summary_ExpectedAddThatDidNotLoad_IsFailed()
    {
        var summary = ReloadSummary.Compute(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), new[] { @"C:\L\Bad", @"C:\L\bad" });
        Assert.Equal(1, summary.Failed);
        Assert.Equal(0, summary.Added);
    }

    [Fact]
    public void Summary_FolderBothUpdatedAndExpectedAdded_CountsOnce()
    {
        var ok = ReloadSummary.Compute(new[] { "M" }, new[] { "M" }, new[] { "M" }, new[] { "M" });
        Assert.Equal("1 map updated", ok.ToToastText());

        var broken = ReloadSummary.Compute(new[] { "M" }, Array.Empty<string>(), new[] { "M" }, new[] { "M" });
        Assert.Equal("1 map failed to load", broken.ToToastText());
    }

    [Fact]
    public void Summary_RescanFindsUnlistedChanges()
    {
        // After a watcher overflow nothing was listed, but the before/after diff still sees changes.
        var summary = ReloadSummary.Compute(new[] { "A", "B" }, new[] { "B", "C", "D" }, Array.Empty<string>(), Array.Empty<string>());
        Assert.Equal("2 maps added, 1 removed", summary.ToToastText());
    }

    [Fact]
    public void Summary_NoChanges_HasNoToast()
    {
        var summary = ReloadSummary.Compute(new[] { "A" }, new[] { "A" }, Array.Empty<string>(), Array.Empty<string>());
        Assert.True(summary.IsEmpty);
        Assert.Null(summary.ToToastText());
        Assert.Equal("no changes", summary.ToString());
    }

    [Theory]
    [InlineData(1, 0, 0, 0, "1 map added")]
    [InlineData(3, 1, 0, 0, "3 maps added, 1 updated")]
    [InlineData(0, 1, 0, 0, "1 map updated")]
    [InlineData(0, 2, 0, 0, "2 maps updated")]
    [InlineData(0, 0, 1, 0, "1 map removed")]
    [InlineData(0, 0, 0, 2, "2 maps failed to load")]
    [InlineData(1, 2, 3, 4, "1 map added, 2 updated, 3 removed, 4 failed to load")]
    [InlineData(0, 1, 0, 1, "1 map updated, 1 failed to load")]
    public void ToastText(int added, int updated, int removed, int failed, string expected)
    {
        var summary = new ReloadSummary(added, updated, removed, failed);
        Assert.Equal(expected, summary.ToToastText());
        Assert.Equal(expected, summary.ToString());
    }

    [Fact]
    public void ChangeBatch_DeduplicatesAndMerges()
    {
        var a = new ChangeBatch(new[] { "X", "x", "Y" }, rescan: false);
        Assert.Equal(2, a.Folders.Count);
        var b = new ChangeBatch(new[] { "Z" }, rescan: true);
        var merged = ChangeBatch.Merge(a, b)!;
        Assert.Equal(3, merged.Folders.Count);
        Assert.True(merged.Rescan);
        Assert.Same(a, ChangeBatch.Merge(a, null));
        Assert.Same(b, ChangeBatch.Merge(null, b));
        Assert.Null(ChangeBatch.Merge(null, null));
        Assert.True(new ChangeBatch(Array.Empty<string>(), false).IsEmpty);
        Assert.False(a.Contains("Q"));
    }

    [Fact]
    public void PlayingMapTracker_NotifiesOncePerPlay()
    {
        var tracker = new PlayingMapTracker();
        var batch = new ChangeBatch(new[] { @"C:\L\Playing" }, false);
        Assert.False(tracker.ShouldNotify(batch));

        tracker.Start(@"c:\l\playing");
        Assert.Equal(@"c:\l\playing", tracker.PlayingFolder);
        Assert.False(tracker.ShouldNotify(null));
        Assert.False(tracker.ShouldNotify(new ChangeBatch(new[] { @"C:\L\Other" }, false)));
        Assert.True(tracker.ShouldNotify(batch));
        Assert.False(tracker.ShouldNotify(batch));

        tracker.Start(@"C:\L\Playing");
        Assert.True(tracker.ShouldNotify(batch));

        tracker.Stop();
        Assert.Null(tracker.PlayingFolder);
        Assert.False(tracker.ShouldNotify(batch));

        tracker.Start(null);
        Assert.False(tracker.ShouldNotify(batch));

        // A rescan-only batch (watcher overflow) does not name the folder: no toast; the menu reload
        // still picks the change up.
        tracker.Start(@"C:\L\Playing");
        Assert.False(tracker.ShouldNotify(new ChangeBatch(Array.Empty<string>(), rescan: true)));
        Assert.True(tracker.ShouldNotify(new ChangeBatch(new[] { @"C:\L\Playing" }, rescan: true)));
    }

    [Fact]
    public void FolderSnapshot_DiffFindsAddedRemovedChanged()
    {
        var previous = new Dictionary<string, long> { ["Same"] = 1, ["Changed"] = 2, ["Gone"] = 3 };
        var current = new Dictionary<string, long> { ["Same"] = 1, ["Changed"] = 5, ["New"] = 4 };
        var diff = FolderSnapshot.Diff(previous, current);
        Assert.Equal(3, diff.Count);
        Assert.Contains("Changed", diff);
        Assert.Contains("New", diff);
        Assert.Contains("Gone", diff);
        Assert.Empty(FolderSnapshot.Diff(current, current));
    }

    [Fact]
    public void FolderSnapshot_MixIsOrderIndependentAndSensitive()
    {
        var ab = FolderSnapshot.Mix(FolderSnapshot.Mix(FolderSnapshot.Empty, 10), 20);
        var ba = FolderSnapshot.Mix(FolderSnapshot.Mix(FolderSnapshot.Empty, 20), 10);
        Assert.Equal(ab, ba);
        Assert.NotEqual(ab, FolderSnapshot.Mix(FolderSnapshot.Mix(FolderSnapshot.Empty, 10), 21));

        // XOR-style stamps cancel out for equal values; this one must not.
        Assert.NotEqual(FolderSnapshot.Empty, FolderSnapshot.Mix(FolderSnapshot.Mix(FolderSnapshot.Empty, 7), 7));
    }

    [Fact]
    public void FolderSnapshot_NameHashIsStableAndCaseInsensitive()
    {
        Assert.Equal(FolderSnapshot.NameHash("Info.dat"), FolderSnapshot.NameHash("INFO.DAT"));
        Assert.NotEqual(FolderSnapshot.NameHash("Info.dat"), FolderSnapshot.NameHash("Expert.dat"));
        Assert.Equal(unchecked((long)14695981039346656037UL), FolderSnapshot.NameHash(string.Empty));
    }
}
