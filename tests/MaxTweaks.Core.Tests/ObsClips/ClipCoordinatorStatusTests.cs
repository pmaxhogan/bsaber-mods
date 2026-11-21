using System;
using System.Linq;
using System.Threading.Tasks;
using MaxTweaks.Core.ObsClips;
using MaxTweaks.Core.ObsRecording;
using MaxTweaks.Core.Tests.ObsRecording;
using Xunit;

namespace MaxTweaks.Core.Tests.ObsClips;

/// <summary>What the OBS status line reads: connected or not, what is wrong, how many clips, and quiet retries.</summary>
public class ClipCoordinatorStatusTests
{
    private const string Replay = "C:/Videos/Replay 2026-01-02 10-00-00.mp4";
    private const string NotReachable = "not connected: OBS is not reachable at 127.0.0.1:4455";

    private static readonly RecordingLabel SongA = new("Song A", "Expert+");

    private readonly FakeConnector _connector = new();
    private readonly FakeClipHost _host = new();
    private readonly ReplayObs _obs = new();
    private readonly ClipCoordinator _coordinator;

    public ClipCoordinatorStatusTests()
    {
        _connector.Configure = _obs.Attach;
        _host.Now = 100.0;
        _coordinator = new ClipCoordinator(_connector, _host, windowsPaths: true);
    }

    private FakeConnection Obs => _connector.Last;

    private async Task Probe()
    {
        _coordinator.Probe();
        await _coordinator.WhenIdleAsync();
    }

    private async Task LevelStarted()
    {
        _coordinator.LevelStarted();
        await _coordinator.WhenIdleAsync();
    }

    private async Task Save()
    {
        _coordinator.Save(new ClipRequest(SongA, "1m23s notes", _host.Now, 40.0, 10.0));
        await _coordinator.WhenIdleAsync();
    }

    private async Task ObsSaved()
    {
        _host.AddFile(Replay);
        Obs.Raise(ReplayObs.Saved(Replay));
        await _coordinator.WhenIdleAsync();
    }

    [Fact]
    public async Task Connected_FollowsTheConnection_NotTheBuffer()
    {
        Assert.False(_coordinator.Connected);
        Assert.Null(_coordinator.Trouble);

        await Probe();

        // Connected, buffer not started yet: nothing is wrong, it starts with the next level.
        Assert.True(_coordinator.Connected);
        Assert.False(_coordinator.Ready);
        Assert.Null(_coordinator.Trouble);

        Obs.Drop("OBS closed the connection");
        await _coordinator.WhenIdleAsync();
        Assert.False(_coordinator.Connected);

        await LevelStarted();
        Assert.True(_coordinator.Connected);
        Assert.True(_coordinator.Ready);

        _coordinator.Disconnect();
        await _coordinator.WhenIdleAsync();
        Assert.False(_coordinator.Connected);
    }

    [Fact]
    public async Task Trouble_ReplayBufferSwitchedOff_ClearsOnceItIsSwitchedOn()
    {
        _obs.Available = false;

        await Probe();

        Assert.True(_coordinator.Connected);
        Assert.Equal("replay buffer off", _coordinator.Trouble);

        _obs.Available = true;
        await Probe();

        Assert.Null(_coordinator.Trouble);
        Assert.Equal("Connected to OBS 31.0.0; the replay buffer (60 s) starts with the next level", _coordinator.Status);
    }

    [Fact]
    public async Task Trouble_BufferTooShort()
    {
        _obs.AdvancedSeconds = "10";

        await Probe();

        Assert.Equal("replay buffer too short", _coordinator.Trouble);

        // Fixed in OBS and seen at the next level.
        _obs.AdvancedSeconds = "60";
        await LevelStarted();
        Assert.Null(_coordinator.Trouble);
        Assert.True(_coordinator.Ready);
    }

    [Fact]
    public async Task Trouble_BufferCannotBeChecked()
    {
        _obs.AdvancedSeconds = null;
        await Probe();
        Assert.Equal("replay buffer unknown", _coordinator.Trouble);

        _obs.AdvancedSeconds = "60";
        _obs.StatusError = new ObsException("OBS did not answer GetReplayBufferStatus within 5 s");
        await Probe();
        Assert.Equal("replay buffer unknown", _coordinator.Trouble);
    }

    [Fact]
    public async Task Trouble_BufferDidNotStart()
    {
        _obs.StartCode = 600;

        await LevelStarted();

        Assert.Equal("replay buffer not started", _coordinator.Trouble);
    }

    [Fact]
    public async Task Trouble_IsGoneWithTheConnection()
    {
        _obs.Available = false;
        await Probe();
        Assert.NotNull(_coordinator.Trouble);

        Obs.Drop("OBS closed the connection");
        await _coordinator.WhenIdleAsync();

        Assert.Null(_coordinator.Trouble);
        Assert.False(_coordinator.Connected);
    }

    [Fact]
    public async Task SavedCount_AddsUpOverLevels_AndSkipsRefusedSaves()
    {
        Assert.Equal(0, _coordinator.SavedCount);

        // Not ready yet: refused, not counted.
        await Save();
        Assert.Equal(0, _coordinator.SavedCount);

        await LevelStarted();
        await Save();
        Assert.Equal(1, _coordinator.SavedCount);
        await ObsSaved();

        _coordinator.LevelEnded();
        await LevelStarted();
        await Save();
        await ObsSaved();
        Assert.Equal(2, _coordinator.SavedCount);

        // OBS says no: refused, not counted.
        _obs.SaveCode = 600;
        await Save();
        Assert.Equal(2, _coordinator.SavedCount);
        Assert.Equal(2, _host.Refusals);

        // Switching the feature off keeps the session's count.
        _coordinator.Disconnect();
        await _coordinator.WhenIdleAsync();
        Assert.Equal(2, _coordinator.SavedCount);
    }

    [Fact]
    public async Task Probe_Repeated_LogsEachFindingOnce()
    {
        _connector.Error = new ObsException("OBS is not reachable at 127.0.0.1:4455");
        await Probe();
        await Probe();
        await Probe();

        Assert.Equal(3, _connector.Attempts);
        Assert.Single(_host.Infos, i => i == NotReachable);

        _connector.Error = null;
        _obs.Available = false;
        await Probe();
        await Probe();
        await Probe();

        Assert.Single(_host.Infos, i => i.StartsWith("not ready: the replay buffer is switched off in OBS", StringComparison.Ordinal));
        Assert.Empty(_host.Notes);
        Assert.Empty(_host.Warnings);
    }

    [Fact]
    public async Task Probe_LogsTheSameFindingAgain_AfterItWasFineInBetween()
    {
        _obs.Available = false;
        await Probe();

        _obs.Available = true;
        await LevelStarted();
        Assert.True(_coordinator.Ready);

        Obs.Raise(ReplayObs.State(started: false));
        await _coordinator.WhenIdleAsync();
        _obs.Active = false;
        _obs.Available = false;
        await Probe();
        await Probe();

        Assert.Equal(2, _host.Infos.Count(i => i.StartsWith("not ready: the replay buffer is switched off in OBS", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Probe_LogsNotReachableAgain_AfterAConnectionInBetween()
    {
        _connector.Error = new ObsException("OBS is not reachable at 127.0.0.1:4455");
        await Probe();

        // OBS comes up in the menu (buffer not started, so never "ready"), then quits again.
        _connector.Error = null;
        await Probe();
        Assert.True(_coordinator.Connected);
        Obs.Drop("OBS closed the connection");
        await _coordinator.WhenIdleAsync();

        _connector.Error = new ObsException("OBS is not reachable at 127.0.0.1:4455");
        await Probe();
        await Probe();

        Assert.Equal(2, _host.Infos.Count(i => i == NotReachable));
    }

    [Fact]
    public async Task Probe_LogsTheSameReasonAgain_AfterTheFeatureWasSwitchedOffAndOn()
    {
        _connector.Error = new ObsException("OBS is not reachable at 127.0.0.1:4455");
        await Probe();

        _coordinator.Disconnect();
        await Probe();

        Assert.Equal(2, _host.Infos.Count(i => i == NotReachable));
    }

    [Fact]
    public async Task Probe_LeavesARunningBufferAlone()
    {
        await LevelStarted();
        var asked = Obs.Requests.Count;

        await Probe();
        await Probe();

        Assert.Equal(asked, Obs.Requests.Count);
        Assert.True(_coordinator.Ready);
    }

    [Fact]
    public async Task Probe_AfterShutdown_DoesNothing()
    {
        await _coordinator.ShutdownAsync(TimeSpan.FromSeconds(1));

        await Probe();

        Assert.Equal(0, _connector.Attempts);
    }
}
