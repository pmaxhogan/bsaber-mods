using System.Linq;
using System.Threading.Tasks;
using MaxTweaks.Core.AutoReload;
using Xunit;

namespace MaxTweaks.Core.Tests.AutoReload;

public class ChangeAggregatorTests
{
    private const double Debounce = 2;
    private readonly FakeClock _clock = new() { Now = 100 };
    private readonly ChangeAggregator _aggregator;

    public ChangeAggregatorTests()
    {
        _aggregator = new ChangeAggregator(_clock);
    }

    [Fact]
    public void NothingRecorded_NothingFlushed()
    {
        Assert.False(_aggregator.HasWork);
        Assert.Null(_aggregator.TryFlush(Debounce));
    }

    [Fact]
    public void Flush_WaitsForQuietPeriod()
    {
        _aggregator.Record(@"C:\L\A");
        Assert.True(_aggregator.HasWork);
        _clock.Advance(1.9);
        Assert.Null(_aggregator.TryFlush(Debounce));
        _clock.Advance(0.1);
        var batch = _aggregator.TryFlush(Debounce);
        Assert.NotNull(batch);
        Assert.Equal(new[] { @"C:\L\A" }, batch!.Folders);
        Assert.False(batch.Rescan);
        Assert.False(_aggregator.HasWork);
        Assert.Null(_aggregator.TryFlush(Debounce));
    }

    [Fact]
    public void NewEvents_RestartTheQuietPeriod_AndBatchTogether()
    {
        _aggregator.Record(@"C:\L\A");
        _clock.Advance(1.5);
        _aggregator.Record(@"C:\L\B");
        _clock.Advance(1.5);
        Assert.Null(_aggregator.TryFlush(Debounce));
        _aggregator.Record(@"C:\L\a"); // same folder, different case
        _clock.Advance(2);
        var batch = _aggregator.TryFlush(Debounce)!;
        Assert.Equal(2, batch.Folders.Count);
        Assert.True(batch.Contains(@"C:\L\A"));
        Assert.True(batch.Contains(@"c:\l\b"));
    }

    [Fact]
    public void Rescan_IsFlushedEvenWithoutFolders()
    {
        _aggregator.RecordRescan();
        Assert.True(_aggregator.HasWork);
        _clock.Advance(2);
        var batch = _aggregator.TryFlush(Debounce)!;
        Assert.True(batch.Rescan);
        Assert.Empty(batch.Folders);
        Assert.False(batch.IsEmpty);
        Assert.False(_aggregator.HasWork);
    }

    [Fact]
    public void Requeue_ReleasesAgainOnNextFlush()
    {
        _aggregator.Record(@"C:\L\A");
        _aggregator.RecordRescan();
        _clock.Advance(2);
        var batch = _aggregator.TryFlush(Debounce)!;
        _aggregator.Requeue(batch);
        var again = _aggregator.TryFlush(Debounce)!;
        Assert.Equal(batch.Folders, again.Folders);
        Assert.True(again.Rescan);
    }

    [Fact]
    public void Requeue_WaitsAgainIfNewEventsArrive()
    {
        _aggregator.Record(@"C:\L\A");
        _clock.Advance(2);
        var batch = _aggregator.TryFlush(Debounce)!;
        _aggregator.Record(@"C:\L\B");
        _aggregator.Requeue(batch);
        Assert.Null(_aggregator.TryFlush(Debounce));
        _clock.Advance(2);
        Assert.Equal(2, _aggregator.TryFlush(Debounce)!.Folders.Count);
    }

    [Fact]
    public void Defer_RechecksLaterWithoutNewEvents()
    {
        _aggregator.Record(@"C:\L\A");
        _clock.Advance(2);
        _aggregator.TryFlush(Debounce);

        Assert.True(_aggregator.Defer(@"C:\L\A", recheckSeconds: 1, maxWaitSeconds: 10));
        Assert.True(_aggregator.HasWork);
        Assert.Equal(new[] { @"C:\L\A" }, _aggregator.Waiting);
        _clock.Advance(0.5);
        Assert.Null(_aggregator.TryFlush(Debounce));
        _clock.Advance(0.5);
        var recheck = _aggregator.TryFlush(Debounce)!;
        Assert.Equal(new[] { @"C:\L\A" }, recheck.Folders);
        Assert.False(_aggregator.HasWork);

        _aggregator.Resolve(@"C:\L\A");
        Assert.Empty(_aggregator.Waiting);
    }

    [Fact]
    public void Defer_GivesUpAfterMaxWait()
    {
        Assert.True(_aggregator.Defer(@"C:\L\A", 1, 3));
        _clock.Advance(1);
        Assert.NotNull(_aggregator.TryFlush(Debounce));
        Assert.True(_aggregator.Defer(@"C:\L\A", 1, 3));
        _clock.Advance(2);
        Assert.NotNull(_aggregator.TryFlush(Debounce));
        Assert.False(_aggregator.Defer(@"C:\L\A", 1, 3));
        Assert.Empty(_aggregator.Waiting);
        Assert.False(_aggregator.HasWork);
    }

    [Fact]
    public void NewEvent_ForParkedFolder_RestartsWaitAndUsesDebounce()
    {
        Assert.True(_aggregator.Defer(@"C:\L\A", 1, 3));
        _clock.Advance(2.5);
        _aggregator.Record(@"C:\L\A");

        // Recheck is cancelled: the folder now waits for the normal quiet period.
        _clock.Advance(1);
        Assert.Null(_aggregator.TryFlush(Debounce));
        _clock.Advance(1);
        Assert.NotNull(_aggregator.TryFlush(Debounce));

        // The wait limit restarted at the new event, so another deferral is still allowed.
        Assert.True(_aggregator.Defer(@"C:\L\A", 1, 3));
    }

    [Fact]
    public void Defer_WhilePending_DoesNotScheduleSeparateRecheck()
    {
        _aggregator.Record(@"C:\L\A");
        Assert.True(_aggregator.Defer(@"C:\L\A", 0, 10));
        Assert.Null(_aggregator.TryFlush(Debounce));
        _clock.Advance(2);
        Assert.Single(_aggregator.TryFlush(Debounce)!.Folders);
    }

    [Fact]
    public void PendingAndDueRechecks_FlushTogether()
    {
        Assert.True(_aggregator.Defer(@"C:\L\Waiting", 1, 10));
        _aggregator.Record(@"C:\L\New");
        _clock.Advance(2);
        var batch = _aggregator.TryFlush(Debounce)!;
        Assert.True(batch.Contains(@"C:\L\Waiting"));
        Assert.True(batch.Contains(@"C:\L\New"));
    }

    [Fact]
    public void DueRecheck_FlushesEvenWhilePendingIsStillSettling()
    {
        Assert.True(_aggregator.Defer(@"C:\L\Waiting", 1, 10));
        _clock.Advance(1);
        _aggregator.Record(@"C:\L\New");
        var batch = _aggregator.TryFlush(Debounce)!;
        Assert.Equal(new[] { @"C:\L\Waiting" }, batch.Folders);
        Assert.False(batch.Rescan);
        _clock.Advance(2);
        Assert.Equal(new[] { @"C:\L\New" }, _aggregator.TryFlush(Debounce)!.Folders);
    }

    [Fact]
    public void Requeue_OfParkedFolder_ReleasesItNowAndCancelsTheRecheck()
    {
        Assert.True(_aggregator.Defer(@"C:\L\A", 5, 60));
        _aggregator.Requeue(new ChangeBatch(new[] { @"C:\L\A" }, rescan: false));
        Assert.Equal(new[] { @"C:\L\A" }, _aggregator.TryFlush(Debounce)!.Folders);

        // The recheck was cancelled, so it does not come back on its own.
        _clock.Advance(10);
        Assert.Null(_aggregator.TryFlush(Debounce));
        Assert.False(_aggregator.HasWork);

        // Its wait limit still runs from the first deferral (no new disk event happened).
        _clock.Advance(50);
        Assert.False(_aggregator.Defer(@"C:\L\A", 5, 60));
    }

    [Fact]
    public void Clear_DropsEverything()
    {
        _aggregator.Record(@"C:\L\A");
        _aggregator.RecordRescan();
        _aggregator.Defer(@"C:\L\B", 1, 10);
        _aggregator.Clear();
        Assert.False(_aggregator.HasWork);
        Assert.Empty(_aggregator.Waiting);
        _clock.Advance(5);
        Assert.Null(_aggregator.TryFlush(Debounce));
    }

    [Fact]
    public void Record_IsThreadSafe()
    {
        Parallel.For(0, 1000, i => _aggregator.Record(@"C:\L\Map" + (i % 50)));
        _clock.Advance(2);
        Assert.Equal(50, _aggregator.TryFlush(Debounce)!.Folders.Distinct().Count());
    }
}
