using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MaxTweaks.Core.ObsClips;
using MaxTweaks.Core.ObsRecording;
using MaxTweaks.Core.Tests.ObsRecording;
using Xunit;

namespace MaxTweaks.Core.Tests.ObsClips;

public class ClipCoordinatorTests
{
    private const string Replay = "C:/Videos/Replay 2026-01-02 10-00-00.mp4";
    private const string Clip = "C:/Videos/Replay 2026-01-02 10-00-00 - Song A - Expert+ - 1m23s notes.mp4";
    private const string Uncut = "C:/Videos/Replay 2026-01-02 10-00-00 - Song A - Expert+ - 1m23s notes uncut.mp4";
    private const string Running = "Connected to OBS 31.0.0; replay buffer running (60 s)";

    private static readonly RecordingLabel SongA = new("Song A", "Expert+");

    private readonly FakeConnector _connector = new();
    private readonly FakeClipHost _host = new();
    private readonly ReplayObs _obs = new();
    private readonly ClipCoordinator _coordinator;

    public ClipCoordinatorTests()
    {
        _connector.Configure = _obs.Attach;
        _host.Now = 100.0;

        // Windows path rules, as in the game, whatever system the tests run on.
        _coordinator = new ClipCoordinator(_connector, _host, windowsPaths: true);
    }

    private FakeConnection Obs => _connector.Last;

    private ClipRequest Request(double secondsAgo = 40.0) => new(SongA, "1m23s notes", _host.Now, secondsAgo, 10.0);

    private async Task LevelStarted()
    {
        _coordinator.LevelStarted();
        await _coordinator.WhenIdleAsync();
    }

    private async Task Save(ClipRequest? request = null)
    {
        _coordinator.Save(request ?? Request());
        await _coordinator.WhenIdleAsync();
    }

    private async Task ObsSaved(string? path)
    {
        if (path != null)
        {
            _host.AddFile(path);
        }

        Obs.Raise(ReplayObs.Saved(path));
        await _coordinator.WhenIdleAsync();
    }

    [Fact]
    public async Task LevelStart_StartsTheBuffer_WhenItIsNotRunning()
    {
        Assert.Equal(ClipCoordinator.NotConnectedYet, _coordinator.Status);
        Assert.False(_coordinator.Ready);

        await LevelStarted();

        Assert.Equal(new[] { "GetReplayBufferStatus", "GetProfileParameter", "GetProfileParameter", "StartReplayBuffer" }, Obs.Requests);
        Assert.Equal(new[] { "Output/Mode", "AdvOut/RecRBTime" }, _obs.Parameters);
        Assert.True(_obs.Active);
        Assert.True(_coordinator.Ready);
        Assert.Equal(60, _coordinator.BufferSeconds);
        Assert.Equal(100.0, _coordinator.BufferSince);
        Assert.Equal(Running, _coordinator.Status);
        Assert.Contains("connected to OBS 31.0.0 (obs-websocket 5.5.4)", _host.Infos);
        Assert.Contains("replay buffer started (60 s)", _host.Infos);
        Assert.Empty(_host.Warnings);
        Assert.Empty(_host.Notes);
        Assert.True(_host.StatusChanges >= 1);
    }

    [Fact]
    public async Task BufferAlreadyRunning_IsUsed_AndNeverStopped()
    {
        _obs.Active = true;

        await LevelStarted();
        _host.Now = 500.0;
        await LevelStarted();

        Assert.DoesNotContain("StartReplayBuffer", Obs.Requests);
        Assert.True(_coordinator.Ready);
        Assert.Equal(100.0, _coordinator.BufferSince);
        Assert.Single(_host.Infos, i => i.StartsWith("replay buffer is already running", StringComparison.Ordinal));

        await _coordinator.ShutdownAsync(TimeSpan.FromSeconds(2));

        Assert.DoesNotContain("StopReplayBuffer", Obs.Requests);
        Assert.True(_obs.Active);
        Assert.True(Obs.Disposed);
    }

    [Fact]
    public async Task Shutdown_StopsABufferThisModStarted()
    {
        await LevelStarted();

        await _coordinator.ShutdownAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("StopReplayBuffer", Obs.Requests.Last());
        Assert.False(_obs.Active);
        Assert.Contains("replay buffer stopped (this mod had started it)", _host.Infos);
        Assert.True(Obs.Disposed);

        // Later calls are ignored.
        _coordinator.LevelStarted();
        await _coordinator.ShutdownAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(1, _connector.Attempts);
    }

    [Fact]
    public async Task Disconnect_StopsOwnBuffer_AndTheNextLevelStartsItAgain()
    {
        await LevelStarted();

        _coordinator.Disconnect();
        await _coordinator.WhenIdleAsync();

        Assert.False(_obs.Active);
        Assert.False(_coordinator.Ready);
        Assert.Equal(ClipCoordinator.NotConnectedYet, _coordinator.Status);
        Assert.True(_connector.Connections[0].Disposed);
        Assert.False(_connector.Connections[0].HasSubscribers);

        _host.Now = 300.0;
        await LevelStarted();

        Assert.Equal(2, _connector.Attempts);
        Assert.True(_obs.Active);
        Assert.True(_coordinator.Ready);
        Assert.Equal(300.0, _coordinator.BufferSince);
    }

    [Fact]
    public async Task Probe_LooksButDoesNotStartTheBuffer()
    {
        _coordinator.Probe();
        await _coordinator.WhenIdleAsync();

        Assert.DoesNotContain("StartReplayBuffer", Obs.Requests);
        Assert.False(_obs.Active);
        Assert.False(_coordinator.Ready);
        Assert.Equal(60, _coordinator.BufferSeconds);
        Assert.Equal("Connected to OBS 31.0.0; the replay buffer (60 s) starts with the next level", _coordinator.Status);
        Assert.Empty(_host.Notes);
    }

    [Fact]
    public async Task Probe_FindsARunningBuffer_Ready()
    {
        _obs.Active = true;

        _coordinator.Probe();
        await _coordinator.WhenIdleAsync();

        Assert.True(_coordinator.Ready);
        Assert.Equal(Running, _coordinator.Status);
    }

    [Fact]
    public async Task ObsNotReachable_IsReportedOnce_AndProbeStaysQuiet()
    {
        _connector.Error = new ObsException("OBS is not reachable at 127.0.0.1:4455");

        _coordinator.Probe();
        await _coordinator.WhenIdleAsync();
        Assert.Empty(_host.Notes);
        Assert.Equal("Not connected: OBS is not reachable at 127.0.0.1:4455", _coordinator.Status);

        await LevelStarted();
        await LevelStarted();

        Assert.Equal(new[] { "OBS clips: OBS is not reachable at 127.0.0.1:4455" }, _host.Notes);
        Assert.Single(_host.Warnings);
        Assert.False(_coordinator.Ready);

        // OBS comes up: the next level works without touching anything.
        _connector.Error = null;
        await LevelStarted();
        Assert.True(_coordinator.Ready);
    }

    [Fact]
    public async Task ReplayBufferSwitchedOffInObs_IsReportedOnce()
    {
        _obs.Available = false;

        await LevelStarted();
        await LevelStarted();

        Assert.False(_coordinator.Ready);
        var note = Assert.Single(_host.Notes);
        Assert.Equal("OBS clips: the replay buffer is switched off in OBS (Settings > Output > Replay Buffer > Enable Replay Buffer)", note);
        Assert.Equal("The replay buffer is switched off in OBS (Settings > Output > Replay Buffer > Enable Replay Buffer)", _coordinator.Status);
        Assert.DoesNotContain("StartReplayBuffer", Obs.Requests);
    }

    [Fact]
    public async Task BufferTooShort_IsReported_AndNotStarted()
    {
        _obs.AdvancedSeconds = "10";

        await LevelStarted();

        Assert.False(_coordinator.Ready);
        Assert.False(_obs.Active);
        var note = Assert.Single(_host.Notes);
        Assert.StartsWith("OBS clips: OBS's replay buffer is 10 s long; clips need 20 s or more, 60 s is best", note, StringComparison.Ordinal);
        Assert.Contains("Maximum Replay Time", note, StringComparison.Ordinal);

        // Fixed in OBS: the next level works.
        _obs.AdvancedSeconds = "60";
        await LevelStarted();
        Assert.True(_coordinator.Ready);
    }

    [Theory]
    [InlineData("Simple", 20)]
    [InlineData("simple", 20)]
    [InlineData(null, 20)]
    [InlineData("Advanced", 60)]
    public async Task BufferLength_ComesFromTheOutputModeInUse(string? mode, int expected)
    {
        _obs.Mode = mode;

        await LevelStarted();

        Assert.Equal(expected, _coordinator.BufferSeconds);
        Assert.True(_coordinator.Ready);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("soon")]
    [InlineData("-5")]
    [InlineData("0")]
    public async Task BufferLengthUnreadable_NoClips(string? text)
    {
        _obs.AdvancedSeconds = text;

        await LevelStarted();

        Assert.False(_coordinator.Ready);
        Assert.Equal(new[] { "OBS clips: could not read the replay buffer length from OBS" }, _host.Notes);
        Assert.False(_obs.Active);
    }

    [Fact]
    public async Task StartRefused_IsReported()
    {
        _obs.StartCode = 600;

        await LevelStarted();

        Assert.False(_coordinator.Ready);
        Assert.Equal(new[] { "OBS clips: OBS did not start the replay buffer (code 600)" }, _host.Notes);
    }

    [Fact]
    public async Task Save_CutsTheStretchOut_AndDeletesTheRest()
    {
        await LevelStarted();

        var request = Request(secondsAgo: 40.0);
        await Save(request);

        Assert.Equal("SaveReplayBuffer", Obs.Requests.Last());
        Assert.Empty(_host.Cuts);

        await ObsSaved(Replay);

        var cut = Assert.Single(_host.Cuts);
        Assert.Equal(Replay, cut.Source);
        Assert.Equal(Clip, cut.Target);
        Assert.Equal(40.0 + ClipCut.LeadSeconds, cut.FromEnd, 6);
        Assert.Equal(10.0 + ClipCut.LeadSeconds + ClipCut.TailSeconds, cut.Seconds, 6);
        Assert.Equal(new[] { Replay }, _host.Deleted);
        Assert.Equal(new[] { Clip }, _host.Files);
        Assert.Contains("saved clip " + Clip, _host.Infos);
        Assert.Empty(_host.Warnings);
        Assert.Empty(_host.Notes);
        Assert.Equal(0, _host.Refusals);

        _coordinator.LevelEnded();
        await _coordinator.WhenIdleAsync();
        Assert.Equal(new[] { "OBS clips: 1 clip from this song" }, _host.Notes);

        // The count starts over with the next level.
        await LevelStarted();
        _coordinator.LevelEnded();
        await _coordinator.WhenIdleAsync();
        Assert.Single(_host.Notes);
    }

    [Fact]
    public async Task Cut_AccountsForTheTimeBetweenThePickAndTheSave()
    {
        await LevelStarted();

        // Picked at 100 s; OBS only got to save it 3 s later.
        var request = Request(secondsAgo: 40.0);
        _host.Now = 103.0;
        await Save(request);
        await ObsSaved(Replay);

        Assert.Equal(43.0 + ClipCut.LeadSeconds, Assert.Single(_host.Cuts).FromEnd, 6);
    }

    [Fact]
    public async Task TwoClips_AreCounted_AndNeverOverwriteEachOther()
    {
        await LevelStarted();

        await Save();
        await ObsSaved(Replay);
        await Save();
        _host.AddFile(Clip);
        await ObsSaved(Replay);

        Assert.Equal(Clip.Replace(".mp4", " (2).mp4", StringComparison.Ordinal), _host.Cuts[1].Target);

        _coordinator.LevelEnded();
        await _coordinator.WhenIdleAsync();
        Assert.Equal(new[] { "OBS clips: 2 clips from this song" }, _host.Notes);
    }

    [Fact]
    public async Task KeepFullReplay_LeavesTheWholeFile()
    {
        _host.KeepFullReplay = true;
        await LevelStarted();

        await Save();
        await ObsSaved(Replay);

        Assert.Empty(_host.Deleted);
        Assert.Equal(new[] { Replay, Clip }.OrderBy(f => f, StringComparer.Ordinal), _host.Files);
    }

    [Fact]
    public async Task SavedEventNobodyAskedFor_IsNotTouched()
    {
        await LevelStarted();

        // The player's own save hotkey.
        await ObsSaved(Replay);

        Assert.Empty(_host.Cuts);
        Assert.Empty(_host.Deleted);
        Assert.Equal(new[] { Replay }, _host.Files);
    }

    [Fact]
    public async Task SaveWhileThePreviousOneIsStillBeingSaved_IsRefused()
    {
        await LevelStarted();

        await Save();
        await Save();

        Assert.Equal(1, Obs.Requests.Count(r => r == "SaveReplayBuffer"));
        Assert.Equal(1, _host.Refusals);
        Assert.Contains(_host.Infos, i => i.Contains("skipped: OBS is still saving the previous one", StringComparison.Ordinal));

        await ObsSaved(Replay);
        Assert.Single(_host.Cuts);
    }

    [Fact]
    public async Task SaveWhenNotReady_IsRefused()
    {
        await Save();
        Assert.Equal(1, _host.Refusals);
        Assert.Equal(0, _connector.Attempts);

        _obs.Available = false;
        await LevelStarted();
        await Save();
        Assert.Equal(2, _host.Refusals);
        Assert.DoesNotContain("SaveReplayBuffer", Obs.Requests);
    }

    [Fact]
    public async Task BufferStoppedInObs_SaveRestartsIt()
    {
        await LevelStarted();
        _obs.Active = false;

        await Save();

        Assert.Equal(1, _host.Refusals);
        Assert.True(_obs.Active);
        Assert.True(_coordinator.Ready);
        Assert.Equal(2, Obs.Requests.Count(r => r == "StartReplayBuffer"));
        Assert.Empty(_host.Notes);
    }

    [Fact]
    public async Task SaveRefusedByObs_IsReported()
    {
        await LevelStarted();
        _obs.SaveCode = 600;

        await Save();
        await Save();

        Assert.Equal(2, _host.Refusals);
        Assert.Equal(new[] { "OBS clips: OBS did not save the replay (code 600: nope)" }, _host.Notes);
    }

    [Fact]
    public async Task SaveRequestFails_IsReported()
    {
        await LevelStarted();
        _obs.SaveError = new ObsException("OBS did not answer SaveReplayBuffer within 5 s");

        await Save();

        Assert.Equal(1, _host.Refusals);
        Assert.Equal(new[] { "OBS clips: could not save the replay: OBS did not answer SaveReplayBuffer within 5 s" }, _host.Notes);
    }

    [Fact]
    public async Task CutFails_TheWholeReplayIsKept()
    {
        await LevelStarted();
        _host.CutResult = _ => new ClipCutResult(ClipCutOutcome.Failed, "ffmpeg exited with 1: bad file");

        await Save();
        await ObsSaved(Replay);

        Assert.Equal(ClipCoordinator.CutAttempts, _host.Cuts.Count);
        Assert.Empty(_host.Deleted);
        Assert.Equal((Replay, Uncut), _host.Moves.Single());
        Assert.Contains(_host.Warnings, w => w.Contains("could not cut the clip out of Replay 2026-01-02 10-00-00.mp4: ffmpeg exited with 1: bad file", StringComparison.Ordinal));
        Assert.Contains("kept the whole replay as " + Uncut, _host.Infos);
    }

    [Fact]
    public async Task CutWorksOnTheSecondTry()
    {
        await LevelStarted();
        _host.CutResult = attempt => attempt == 1 ? new ClipCutResult(ClipCutOutcome.Failed, "file in use") : null;

        await Save();
        await ObsSaved(Replay);

        Assert.Equal(2, _host.Cuts.Count);
        Assert.Equal(new[] { Clip }, _host.Files);
        Assert.Empty(_host.Warnings);
    }

    [Fact]
    public async Task NoFfmpeg_IsReportedOnce_AndReplaysAreKeptWhole()
    {
        await LevelStarted();
        _host.CutResult = _ => new ClipCutResult(ClipCutOutcome.ToolMissing, "cannot start 'ffmpeg'");

        await Save();
        await ObsSaved(Replay);
        await Save();
        await ObsSaved("C:/Videos/Replay 2026-01-02 10-05-00.mp4");

        // No retries: it will not appear by itself.
        Assert.Equal(2, _host.Cuts.Count);
        Assert.Equal(2, _host.Moves.Count);
        Assert.Equal(Uncut, _host.Moves[0].To);
        Assert.Single(_host.Notes);
        Assert.Contains("ffmpeg was not found", _host.Notes[0], StringComparison.Ordinal);
        Assert.Single(_host.Warnings);
    }

    [Theory]
    [InlineData("//server/share/Replay.mp4", "it is a network or device path")]
    [InlineData("Replay.mp4", "it is not a full path on a drive of this PC")]
    [InlineData("C:/Videos/../Windows/notes.mp4", "it has relative parts")]
    [InlineData("C:/Windows/system32/drivers/etc/hosts", "it does not have a recording's file extension")]
    public async Task PathFromObs_IsNotTrusted(string path, string why)
    {
        await LevelStarted();

        await Save();
        await ObsSaved(path);

        Assert.Empty(_host.Cuts);
        Assert.Empty(_host.Deleted);
        Assert.Empty(_host.Moves);
        Assert.Contains(_host.Warnings, w => w.StartsWith("not touching the replay for 'Song A' (Expert+) 1m23s notes: " + why, StringComparison.Ordinal));
    }

    [Fact]
    public async Task NoPathFromObs_NothingIsTouched()
    {
        await LevelStarted();

        await Save();
        await ObsSaved(null);

        Assert.Empty(_host.Cuts);
        Assert.Contains(_host.Warnings, w => w.StartsWith("OBS did not report the file name of the replay", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ObsOnAnotherPc_TheReplayStaysThere()
    {
        _connector.Configure = connection =>
        {
            _obs.Attach(connection);
            connection.IsThisPc = false;
        };
        await LevelStarted();

        await Save();
        await ObsSaved(Replay);

        Assert.Empty(_host.Cuts);
        Assert.Empty(_host.Deleted);
        Assert.Contains(_host.Infos, i => i.StartsWith("OBS runs on another PC", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ObsNeverReportsTheSave_GivesUp_AndLaterSavesWork()
    {
        await LevelStarted();

        await Save();
        Assert.Equal(1, _host.FireSaveTimeout());
        await _coordinator.WhenIdleAsync();

        Assert.Contains(_host.Warnings, w => w.Contains("as saved within 15 s", StringComparison.Ordinal));

        // The late report belongs to nobody any more.
        await ObsSaved(Replay);
        Assert.Empty(_host.Cuts);

        await Save();
        await ObsSaved("C:/Videos/Replay 2026-01-02 10-05-00.mp4");
        Assert.Single(_host.Cuts);
        Assert.Equal(0, _host.FireSaveTimeout());
    }

    [Fact]
    public async Task ObsDisconnectsDuringASave_IsReported_AndTheNextLevelReconnects()
    {
        await LevelStarted();
        await Save();

        Obs.Drop("OBS closed the connection");
        await _coordinator.WhenIdleAsync();

        Assert.False(_coordinator.Ready);
        Assert.Equal("Not connected: OBS closed the connection", _coordinator.Status);
        Assert.Contains(_host.Warnings, w => w.StartsWith("OBS disconnected before the replay for", StringComparison.Ordinal));

        // OBS was restarted: its buffer is not running any more.
        _obs.Active = false;
        _host.Now = 900.0;
        await LevelStarted();

        Assert.Equal(2, _connector.Attempts);
        Assert.True(_coordinator.Ready);
        Assert.Equal(900.0, _coordinator.BufferSince);
    }

    [Fact]
    public async Task BufferStoppedAndStartedByHandInObs_IsFollowed()
    {
        await LevelStarted();

        _obs.Active = false;
        Obs.Raise(ReplayObs.State(started: false));
        await _coordinator.WhenIdleAsync();

        Assert.False(_coordinator.Ready);
        Assert.Equal("Connected to OBS 31.0.0; the replay buffer starts with the next level", _coordinator.Status);

        _obs.Active = true;
        _host.Now = 250.0;
        Obs.Raise(ReplayObs.State(started: true));
        await _coordinator.WhenIdleAsync();

        Assert.True(_coordinator.Ready);
        Assert.Equal(250.0, _coordinator.BufferSince);
        Assert.Equal(1, Obs.Requests.Count(r => r == "StartReplayBuffer"));

        // Stopped by hand in between, so it is not this mod's to stop any more.
        await _coordinator.ShutdownAsync(TimeSpan.FromSeconds(2));
        Assert.DoesNotContain("StopReplayBuffer", Obs.Requests);
    }

    [Fact]
    public async Task OwnStartEvent_ChangesNothing()
    {
        await LevelStarted();
        var requests = Obs.Requests.Count;

        Obs.Raise(ReplayObs.State(started: true));
        await _coordinator.WhenIdleAsync();

        Assert.Equal(requests, Obs.Requests.Count);
        Assert.Equal(100.0, _coordinator.BufferSince);
    }

    [Fact]
    public async Task WholeReplayStillLocked_DeleteIsRetried()
    {
        await LevelStarted();
        _host.DeleteError = attempt => attempt < 3 ? new IOException("in use") : null;

        await Save();
        await ObsSaved(Replay);

        Assert.Equal(3, _host.DeleteAttempts);
        Assert.Equal(new[] { Clip }, _host.Files);
        Assert.Empty(_host.Warnings);
    }

    [Fact]
    public async Task WholeReplayNeverUnlocks_GivesUpWithAWarning()
    {
        await LevelStarted();
        _host.DeleteError = _ => new UnauthorizedAccessException("denied");

        await Save();
        await ObsSaved(Replay);

        Assert.Equal(ClipCoordinator.FileAttempts, _host.DeleteAttempts);
        Assert.Contains(_host.Warnings, w => w.StartsWith("could not delete the whole replay", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UncutRename_IsRetried_AndGivesUpQuietlyWhenTheFileIsGone()
    {
        await LevelStarted();
        _host.CutResult = _ => new ClipCutResult(ClipCutOutcome.Failed, "no");
        _host.MoveError = attempt => attempt == 1 ? new IOException("in use") : null;

        await Save();
        await ObsSaved(Replay);
        Assert.Equal(2, _host.MoveAttempts);
        Assert.Equal(new[] { Uncut }, _host.Files);

        // Next time the file vanished before the rename.
        _host.MoveError = _ => new FileNotFoundException("gone");
        await Save();
        await ObsSaved("C:/Videos/Replay 2026-01-02 10-05-00.mp4");
        Assert.Contains(_host.Warnings, w => w.StartsWith("replay not found at", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Shutdown_WaitsForASaveInFlight_ThenStopsTheBuffer()
    {
        await LevelStarted();
        await Save();

        var shutdown = _coordinator.ShutdownAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(shutdown.IsCompleted);

        _host.AddFile(Replay);
        Obs.Raise(ReplayObs.Saved(Replay));
        await shutdown;

        Assert.Equal(new[] { Clip }, _host.Files);
        Assert.Equal("StopReplayBuffer", Obs.Requests.Last());
        Assert.True(Obs.Disposed);
    }

    [Fact]
    public async Task Shutdown_DoesNotWaitForeverForObs()
    {
        await LevelStarted();
        await Save();

        var clock = System.Diagnostics.Stopwatch.StartNew();
        await _coordinator.ShutdownAsync(TimeSpan.FromMilliseconds(300));

        Assert.True(clock.ElapsedMilliseconds < 3000);
        Assert.True(Obs.Disposed);
    }

    [Fact]
    public async Task StatusRequestFails_IsReported_AndProbeStaysQuiet()
    {
        _obs.StatusError = new ObsException("OBS did not answer GetReplayBufferStatus within 5 s");

        _coordinator.Probe();
        await _coordinator.WhenIdleAsync();
        Assert.Empty(_host.Notes);
        Assert.Contains(_host.Infos, i => i.StartsWith("not ready: could not check the replay buffer", StringComparison.Ordinal));

        await LevelStarted();

        Assert.False(_coordinator.Ready);
        Assert.Equal(new[] { "OBS clips: could not check the replay buffer: OBS did not answer GetReplayBufferStatus within 5 s" }, _host.Notes);
        Assert.Equal("Could not check the replay buffer: OBS did not answer GetReplayBufferStatus within 5 s", _coordinator.Status);
    }

    [Fact]
    public async Task HostFailures_NeverStopTheCoordinator()
    {
        // A broken status line and a cut that throws: logged, and the next clip still works.
        _host.ThrowOnStatusChanged = true;
        await LevelStarted();
        Assert.True(_coordinator.Ready);

        _host.CutError = new InvalidOperationException("boom");
        await Save();
        await ObsSaved(Replay);
        Assert.Contains(_host.Warnings, w => w.Contains("1m23s notes failed: boom", StringComparison.Ordinal));

        _host.CutError = null;
        await Save();
        await ObsSaved("C:/Videos/Replay 2026-01-02 10-05-00.mp4");
        Assert.Contains(_host.Infos, i => i.StartsWith("saved clip C:/Videos/Replay 2026-01-02 10-05-00", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WholeReplayAlreadyGone_IsFine()
    {
        await LevelStarted();
        _host.DeleteError = _ => new FileNotFoundException("gone");

        await Save();
        await ObsSaved(Replay);

        Assert.Equal(1, _host.DeleteAttempts);
        Assert.Empty(_host.Warnings);
    }

    [Fact]
    public async Task UncutRename_GivesUpWithAWarning()
    {
        await LevelStarted();
        _host.CutResult = _ => new ClipCutResult(ClipCutOutcome.Failed, "no");
        _host.MoveError = _ => new IOException("in use");

        await Save();
        await ObsSaved(Replay);

        Assert.Equal(ClipCoordinator.FileAttempts, _host.MoveAttempts);
        Assert.Contains(_host.Warnings, w => w.StartsWith("could not rename " + Replay, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Shutdown_WhenObsIsGone_OrRefusesToStop_StillFinishes()
    {
        await LevelStarted();
        _obs.StopError = new ObsException("not connected to OBS");

        await _coordinator.ShutdownAsync(TimeSpan.FromSeconds(2));

        Assert.Contains(_host.Infos, i => i.StartsWith("could not stop the replay buffer", StringComparison.Ordinal));
        Assert.True(Obs.Disposed);
    }

    [Fact]
    public async Task Shutdown_AfterObsDisconnected_HasNothingToStop()
    {
        await LevelStarted();
        Obs.Drop("OBS closed the connection");
        await _coordinator.WhenIdleAsync();

        await _coordinator.ShutdownAsync(TimeSpan.FromSeconds(2));

        Assert.DoesNotContain("StopReplayBuffer", _connector.Connections[0].Requests);
    }

    [Fact]
    public void Request_AlwaysHasANamePart()
    {
        Assert.Equal("clip", new ClipRequest(SongA, "  ", 0, 0, 10).Extra);
        Assert.Equal("1m23s notes", new ClipRequest(SongA, " 1m23s notes ", 0, 0, 10).Extra);
        Assert.Equal("'Song A' (Expert+) 1m23s notes", Request().ToString());
    }
}
