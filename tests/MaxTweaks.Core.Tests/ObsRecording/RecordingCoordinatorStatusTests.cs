using System;
using System.Linq;
using System.Threading.Tasks;
using MaxTweaks.Core.ObsRecording;
using Xunit;

namespace MaxTweaks.Core.Tests.ObsRecording;

/// <summary>What the OBS status line reads: connected or not, how many recordings, and quiet retries.</summary>
public class RecordingCoordinatorStatusTests
{
    private const string PathA = "C:/Videos/2026-01-02 10-00-00.mp4";
    private const string PathB = "C:/Videos/2026-01-02 10-05-00.mp4";
    private const string NotReachable = "not connected: OBS is not reachable at 127.0.0.1:4455";

    private static readonly RecordingLabel SongA = new("Song A", "Expert+");

    private readonly FakeConnector _connector = new();
    private readonly FakeHost _host = new();
    private readonly RecordingCoordinator _coordinator;

    public RecordingCoordinatorStatusTests()
    {
        _coordinator = new RecordingCoordinator(_connector, _host, windowsPaths: true);
    }

    private FakeConnection Obs => _connector.Last;

    private async Task Probe()
    {
        _coordinator.Probe();
        await _coordinator.WhenIdleAsync();
    }

    private async Task Record(string path)
    {
        _host.AddFile(path);
        _coordinator.LevelStarted(SongA);
        await _coordinator.WhenIdleAsync();
        Obs.StopPath = path;
        _coordinator.LevelEnded(SongA);
        await _coordinator.WhenIdleAsync();
        Obs.Raise(ObsTestData.StoppedEvent(path));
        await _coordinator.WhenIdleAsync();
    }

    [Fact]
    public async Task Connected_FollowsTheConnection()
    {
        Assert.False(_coordinator.Connected);

        await Probe();
        Assert.True(_coordinator.Connected);

        Obs.Drop("OBS closed the connection");
        await _coordinator.WhenIdleAsync();
        Assert.False(_coordinator.Connected);

        await Probe();
        Assert.True(_coordinator.Connected);

        _coordinator.Disconnect();
        await _coordinator.WhenIdleAsync();
        Assert.False(_coordinator.Connected);
    }

    [Fact]
    public async Task Connected_StaysFalse_WhenObsIsNotThere()
    {
        _connector.Error = new ObsException("OBS is not reachable at 127.0.0.1:4455");

        await Probe();

        Assert.False(_coordinator.Connected);
    }

    [Fact]
    public async Task SavedCount_CountsOurFinishedRecordings()
    {
        Assert.Equal(0, _coordinator.SavedCount);

        await Record(PathA);
        Assert.Equal(1, _coordinator.SavedCount);

        await Record(PathB);
        Assert.Equal(2, _coordinator.SavedCount);

        // Switching the feature off and on again keeps the session's count.
        _coordinator.Disconnect();
        await _coordinator.WhenIdleAsync();
        Assert.Equal(2, _coordinator.SavedCount);
    }

    [Fact]
    public async Task SavedCount_IgnoresRecordingsThatAreNotOurs()
    {
        await Probe();

        // The player's own recording ends: nothing of ours.
        Obs.Raise(ObsTestData.StoppedEvent(PathA));
        await _coordinator.WhenIdleAsync();
        Assert.Equal(0, _coordinator.SavedCount);

        // A level that OBS refused to record (it was already recording) saves nothing either.
        _connector.Last.Respond = type => type == "StartRecord" ? ObsTestData.Fail(type, 500) : ObsTestData.Ok(type);
        _coordinator.LevelStarted(SongA);
        _coordinator.LevelEnded(SongA);
        await _coordinator.WhenIdleAsync();
        Assert.Equal(0, _coordinator.SavedCount);
    }

    [Fact]
    public async Task SavedCount_CountsARecordingWhoseFinishObsNeverReported()
    {
        _coordinator.LevelStarted(SongA);
        _coordinator.LevelEnded(SongA);
        await _coordinator.WhenIdleAsync();
        Assert.Equal(0, _coordinator.SavedCount);

        Assert.Equal(1, _host.FireStopTimeout());
        await _coordinator.WhenIdleAsync();

        Assert.Equal(1, _coordinator.SavedCount);
    }

    [Fact]
    public async Task Probe_Repeated_LogsTheReasonOnce()
    {
        _connector.Error = new ObsException("OBS is not reachable at 127.0.0.1:4455");

        await Probe();
        await Probe();
        await Probe();

        Assert.Equal(3, _connector.Attempts);
        Assert.Single(_host.Infos, i => i == NotReachable);
        Assert.Empty(_host.Notes);
        Assert.Empty(_host.Warnings);

        // A different reason is new information.
        _connector.Error = new ObsException("OBS rejected the WebSocket password");
        await Probe();
        await Probe();
        Assert.Single(_host.Infos, i => i == "not connected: OBS rejected the WebSocket password");
    }

    [Fact]
    public async Task Probe_LogsTheSameReasonAgain_AfterAConnectionInBetween()
    {
        _connector.Error = new ObsException("OBS is not reachable at 127.0.0.1:4455");
        await Probe();

        _connector.Error = null;
        await Probe();
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
    public async Task Probe_OneAtATime()
    {
        _connector.Error = new ObsException("OBS is not reachable at 127.0.0.1:4455");
        var hanging = _host.BlockOnInfo("not connected");

        _coordinator.Probe();
        await hanging;
        _coordinator.Probe();
        _coordinator.Probe();
        _host.ReleaseInfo();
        await _coordinator.WhenIdleAsync();

        Assert.Equal(1, _connector.Attempts);

        // The next one goes through again.
        await Probe();
        Assert.Equal(2, _connector.Attempts);
    }

    [Fact]
    public async Task Probe_AfterShutdown_DoesNothing()
    {
        await _coordinator.ShutdownAsync(TimeSpan.FromSeconds(1));

        await Probe();

        Assert.Equal(0, _connector.Attempts);
    }
}
