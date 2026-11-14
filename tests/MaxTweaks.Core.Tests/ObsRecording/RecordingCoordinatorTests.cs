using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MaxTweaks.Core.ObsRecording;
using Xunit;

namespace MaxTweaks.Core.Tests.ObsRecording;

public class RecordingCoordinatorTests
{
    private const string PathA = "C:/Videos/2026-01-02 10-00-00.mp4";
    private const string PathB = "C:/Videos/2026-01-02 10-05-00.mp4";
    private const string RenamedA = "C:/Videos/2026-01-02 10-00-00 - Song A - Expert+.mp4";

    private static readonly RecordingLabel SongA = new("Song A", "Expert+");
    private static readonly RecordingLabel SongB = new("Song B", "Hard");

    private readonly FakeConnector _connector = new();
    private readonly FakeHost _host = new();
    private readonly RecordingCoordinator _coordinator;

    public RecordingCoordinatorTests()
    {
        // Windows path rules, as in the game, whatever system the tests run on.
        _coordinator = new RecordingCoordinator(_connector, _host, windowsPaths: true);
    }

    private FakeConnection Obs => _connector.Last;

    private async Task Start(RecordingLabel label)
    {
        _coordinator.LevelStarted(label);
        await _coordinator.WhenIdleAsync();
    }

    private async Task End(RecordingLabel label)
    {
        _coordinator.LevelEnded(label);
        await _coordinator.WhenIdleAsync();
    }

    private async Task ObsReportsStopped(string? path)
    {
        Obs.Raise(ObsTestData.StoppedEvent(path));
        await _coordinator.WhenIdleAsync();
    }

    [Fact]
    public async Task Level_StartsStopsAndRenames()
    {
        Assert.Equal(RecordingCoordinator.NotConnectedYet, _coordinator.Status);

        await Start(SongA);

        Assert.Equal(1, _connector.Attempts);
        Assert.True(Obs.Started);
        Assert.Equal(new[] { "StartRecord" }, Obs.Requests);
        Assert.Equal("Recording 'Song A' (Expert+)", _coordinator.Status);
        Assert.Contains("connected to OBS 31.0.0 (obs-websocket 5.5.4)", _host.Infos);
        Assert.Contains("record started for 'Song A' (Expert+)", _host.Infos);

        await End(SongA);

        Assert.Equal(new[] { "StartRecord", "StopRecord" }, Obs.Requests);
        Assert.Equal("Saving the recording", _coordinator.Status);
        Assert.Empty(_host.Moves);

        await ObsReportsStopped(PathA);

        Assert.Equal((PathA, RenamedA), _host.Moves.Single());
        Assert.Contains("saved " + RenamedA, _host.Infos);
        Assert.Equal(new[] { "Recording saved: 2026-01-02 10-00-00 - Song A - Expert+.mp4" }, _host.Notes);
        Assert.Empty(_host.Warnings);
        Assert.Equal("Connected to OBS 31.0.0", _coordinator.Status);
        Assert.True(_host.StatusChanges >= 3);
    }

    [Fact]
    public async Task Rename_WaitsForTheStoppedEvent_NotOtherStates()
    {
        await Start(SongA);
        await End(SongA);

        Obs.Raise(ObsTestData.RecordState("OBS_WEBSOCKET_OUTPUT_STOPPING"));
        Obs.Raise(new ObsEvent("SomethingElse", null));
        await _coordinator.WhenIdleAsync();

        Assert.Empty(_host.Moves);
        Assert.Equal("Saving the recording", _coordinator.Status);
    }

    [Fact]
    public async Task FileIsNamedAfterTheLabelAtLevelEnd()
    {
        await Start(new RecordingLabel("Song A", "Expert"));
        await End(SongA);
        await ObsReportsStopped(PathA);

        Assert.Equal(RenamedA, _host.Moves.Single().To);
    }

    [Fact]
    public async Task EventPath_IsUsed_WhenTheStopResponseHadNone()
    {
        _connector.Configure = c => c.StopPath = null;
        await Start(SongA);
        await End(SongA);
        await ObsReportsStopped(PathA);

        Assert.Equal((PathA, RenamedA), _host.Moves.Single());
    }

    [Fact]
    public async Task ResponsePath_IsUsed_WhenTheEventHasNone()
    {
        await Start(SongA);
        await End(SongA);
        await ObsReportsStopped(null);

        Assert.Equal((PathA, RenamedA), _host.Moves.Single());
    }

    [Fact]
    public async Task NoPathAtAll_KeepsTheName_AndWarns()
    {
        _connector.Configure = c => c.StopPath = null;
        await Start(SongA);
        await End(SongA);
        await ObsReportsStopped(null);

        Assert.Empty(_host.Moves);
        Assert.Contains(_host.Warnings, w => w.Contains("did not report the file name"));
    }

    [Theory]
    [InlineData(@"\\fileserver\share\2026-01-02 10-00-00.mp4", "network or device path")]
    [InlineData("//fileserver/share/2026-01-02 10-00-00.mp4", "network or device path")]
    [InlineData(@"\\?\C:\Videos\2026-01-02 10-00-00.mp4", "network or device path")]
    [InlineData(@"\\.\PhysicalDrive0\a.mp4", "network or device path")]
    [InlineData("2026-01-02 10-00-00.mp4", "not a full path")]
    [InlineData("/Videos/2026-01-02 10-00-00.mp4", "not a full path")]
    [InlineData("C:/Videos/../Windows/a.mp4", "relative parts")]
    [InlineData("C:/Users/me/Documents/thesis.docx", "recording's file extension")]
    [InlineData("C:/Windows/System32/drivers/etc/hosts", "recording's file extension")]
    [InlineData("C:/Videos/a.mp4:hidden", "stream or a device")]
    public async Task PathFromObs_ThatIsNotALocalRecording_IsNeverTouched(string reported, string why)
    {
        _connector.Configure = c => c.StopPath = reported;
        await Start(SongA);
        await End(SongA);
        await ObsReportsStopped(reported);

        Assert.Equal(0, _host.MoveAttempts);
        Assert.Empty(_host.Moves);
        var warning = Assert.Single(_host.Warnings);
        Assert.StartsWith("not renaming the recording of 'Song A' (Expert+): ", warning);
        Assert.Contains(why, warning);
        Assert.DoesNotContain(_host.Notes, n => n.StartsWith("Recording saved"));
        Assert.Equal("Connected to OBS 31.0.0", _coordinator.Status);
    }

    [Fact]
    public async Task PathFromObs_OnlyInTheEvent_IsCheckedToo()
    {
        _connector.Configure = c => c.StopPath = null;
        await Start(SongA);
        await End(SongA);
        await ObsReportsStopped(@"\\fileserver\share\a.mp4");

        Assert.Equal(0, _host.MoveAttempts);
        Assert.Contains(_host.Warnings, w => w.Contains("network or device path"));
    }

    [Fact]
    public async Task RefusedPath_IsLoggedWithoutControlCharacters_AndCut()
    {
        var reported = "C:/Videos/a\r\n[ObsRecording] forged line" + new string('x', 400) + ".mp4";
        _connector.Configure = c => c.StopPath = reported;
        await Start(SongA);
        await End(SongA);
        await ObsReportsStopped(reported);

        var warning = Assert.Single(_host.Warnings);
        Assert.Contains("control characters", warning);
        Assert.DoesNotContain("\r", warning);
        Assert.DoesNotContain("\n", warning);
        Assert.Contains("a??[ObsRecording] forged line", warning);
        Assert.True(warning.Length < 320, "warning is " + warning.Length + " characters");
        Assert.Equal(0, _host.MoveAttempts);
    }

    [Fact]
    public async Task ObsOnAnotherPc_ItsFileIsNotRenamedHere()
    {
        _connector.Configure = c => c.IsThisPc = false;
        await Start(SongA);
        await End(SongA);
        await ObsReportsStopped(PathA);

        Assert.Equal(new[] { "StartRecord", "StopRecord" }, Obs.Requests);
        Assert.Equal(0, _host.MoveAttempts);
        Assert.Contains("OBS runs on another PC, so the recording of 'Song A' (Expert+) keeps OBS's name", _host.Infos);
        Assert.Empty(_host.Notes);
        Assert.Empty(_host.Warnings);
        Assert.Equal("Connected to OBS 31.0.0", _coordinator.Status);
    }

    [Fact]
    public async Task PathRules_DefaultToTheSystemTheGameRunsOn()
    {
        var coordinator = new RecordingCoordinator(_connector, _host);
        var windows = Path.DirectorySeparatorChar == '\\';
        var native = windows ? "C:/Videos/a.mp4" : "/videos/a.mp4";
        var foreign = windows ? "/videos/b.mp4" : "C:/Videos/b.mp4";

        foreach (var reported in new[] { native, foreign })
        {
            _connector.Configure = c => c.StopPath = reported;
            coordinator.Disconnect();
            coordinator.LevelStarted(SongA);
            coordinator.LevelEnded(SongA);
            await coordinator.WhenIdleAsync();
            Obs.Raise(ObsTestData.StoppedEvent(reported));
            await coordinator.WhenIdleAsync();
        }

        Assert.Equal(native, _host.Moves.Single().From);
        Assert.Single(_host.Warnings, w => w.StartsWith("not renaming"));
    }

    [Fact]
    public async Task StoppedEventForAnotherFile_IsIgnored()
    {
        await Start(SongA);
        await End(SongA);

        await ObsReportsStopped(PathB);
        Assert.Empty(_host.Moves);
        Assert.Equal("Saving the recording", _coordinator.Status);

        await ObsReportsStopped(PathA.Replace('/', '\\').ToUpperInvariant());
        Assert.Single(_host.Moves);
    }

    [Fact]
    public async Task Restart_QueuesTheNewStartUntilThePreviousFileIsFinished()
    {
        await Start(SongA);
        _coordinator.LevelEnded(SongA);
        _coordinator.LevelStarted(SongB);
        await _coordinator.WhenIdleAsync();

        Assert.Equal(new[] { "StartRecord", "StopRecord" }, Obs.Requests);
        Assert.Contains(_host.Infos, i => i.Contains("waits until OBS has finished the previous file"));

        await ObsReportsStopped(PathA);

        Assert.Equal(new[] { "StartRecord", "StopRecord", "StartRecord" }, Obs.Requests);
        Assert.Equal((PathA, RenamedA), _host.Moves.Single());
        Assert.Equal("Recording 'Song B' (Hard)", _coordinator.Status);

        Obs.StopPath = PathB;
        await End(SongB);
        await ObsReportsStopped(PathB);

        Assert.Equal(2, _host.Moves.Count);
        Assert.Equal("C:/Videos/2026-01-02 10-05-00 - Song B - Hard.mp4", _host.Moves[1].To);
        Assert.Equal(1, _connector.Attempts);
    }

    [Fact]
    public async Task Restart_ThenQuitBeforeTheOldFileIsFinished_StartsNothingNew()
    {
        await Start(SongA);
        _coordinator.LevelEnded(SongA);
        _coordinator.LevelStarted(SongB);
        _coordinator.LevelEnded(SongB);
        await _coordinator.WhenIdleAsync();
        await ObsReportsStopped(PathA);

        Assert.Equal(new[] { "StartRecord", "StopRecord" }, Obs.Requests);
        Assert.Single(_host.Moves);
        Assert.Contains("level ended before its recording could start", _host.Infos);
        Assert.Equal("Connected to OBS 31.0.0", _coordinator.Status);
    }

    [Fact]
    public async Task TwoQueuedStarts_OnlyTheLatestIsStarted()
    {
        await Start(SongA);
        _coordinator.LevelEnded(SongA);
        _coordinator.LevelStarted(new RecordingLabel("Skipped", "Easy"));
        _coordinator.LevelStarted(SongB);
        await _coordinator.WhenIdleAsync();
        await ObsReportsStopped(PathA);

        Assert.Equal("Recording 'Song B' (Hard)", _coordinator.Status);
        Assert.Equal(2, Obs.Requests.Count(r => r == "StartRecord"));
    }

    [Fact]
    public async Task StartWithoutAnEnd_FinishesTheOldRecordingFirst()
    {
        await Start(SongA);
        await Start(SongB);

        Assert.Equal(new[] { "StartRecord", "StopRecord" }, Obs.Requests);

        await ObsReportsStopped(PathA);

        Assert.Equal((PathA, RenamedA), _host.Moves.Single());
        Assert.Equal("Recording 'Song B' (Hard)", _coordinator.Status);
    }

    [Fact]
    public async Task ObsAlreadyRecording_IsLeftAlone()
    {
        _connector.Configure = c => c.Respond = type => type == "StartRecord" ? ObsTestData.Fail(type, 500) : ObsTestData.Ok(type);

        await Start(SongA);

        Assert.Single(_host.Notes);
        Assert.Contains("already recording", _host.Notes[0]);
        Assert.Equal("Connected to OBS 31.0.0", _coordinator.Status);

        await End(SongA);
        await ObsReportsStopped(PathA);

        Assert.Equal(new[] { "StartRecord" }, Obs.Requests);
        Assert.Empty(_host.Moves);

        // Same situation on the next level: said once, not once per level.
        await Start(SongB);
        await End(SongB);

        Assert.Single(_host.Notes);
        Assert.Single(_host.Warnings);
        Assert.DoesNotContain("StopRecord", Obs.Requests);
    }

    [Fact]
    public async Task StartRefusedForAnotherReason_IsReportedWithTheCode()
    {
        _connector.Configure = c => c.Respond = type => ObsTestData.Fail(type, 702, "encoder missing");

        await Start(SongA);
        await End(SongA);

        Assert.Contains("OBS did not start recording (code 702: encoder missing)", _host.Warnings);
        Assert.Equal(new[] { "StartRecord" }, Obs.Requests);
    }

    [Fact]
    public async Task StartRequestFails_IsReported_AndNothingIsStoppedLater()
    {
        _connector.Configure = c => c.Respond = type => throw new ObsException("OBS did not answer StartRecord within 5 s");

        await Start(SongA);
        await End(SongA);

        Assert.Equal(new[] { "StartRecord" }, Obs.Requests);
        Assert.Contains("could not start the recording: OBS did not answer StartRecord within 5 s", _host.Warnings);
        Assert.Single(_host.Notes);
    }

    [Fact]
    public async Task ObsUnreachable_IsSaidOncePerEpisode_AndRecovers()
    {
        _connector.Error = new ObsException("OBS is not reachable at 127.0.0.1:4455");

        await Start(SongA);
        await End(SongA);
        await Start(SongB);
        await End(SongB);

        Assert.Equal(2, _connector.Attempts);
        Assert.Equal(new[] { "OBS recording: OBS is not reachable at 127.0.0.1:4455" }, _host.Notes);
        Assert.Single(_host.Warnings);
        Assert.Equal("Not connected: OBS is not reachable at 127.0.0.1:4455", _coordinator.Status);

        // OBS comes up: the next level records, and a later outage is a new episode.
        _connector.Error = null;
        await Start(SongA);
        Assert.Equal(new[] { "StartRecord" }, Obs.Requests);
        await End(SongA);
        await ObsReportsStopped(PathA);
        Assert.Single(_host.Moves);

        Obs.Drop("OBS closed the connection");
        await _coordinator.WhenIdleAsync();
        _connector.Error = new ObsException("OBS is not reachable at 127.0.0.1:4455");
        await Start(SongB);

        Assert.Equal(2, _host.Notes.Count(n => n.Contains("not reachable")));
    }

    [Fact]
    public async Task UnexpectedConnectError_IsReportedWithItsType()
    {
        _connector.Error = new InvalidOperationException("boom");

        await Start(SongA);

        Assert.Contains("InvalidOperationException: boom", _host.Warnings);
    }

    [Fact]
    public async Task DisconnectMidLevel_ReconnectsToStop()
    {
        await Start(SongA);
        var first = Obs;

        first.Drop("the connection to OBS was lost (reset)");
        await _coordinator.WhenIdleAsync();

        Assert.Equal("Not connected: the connection to OBS was lost (reset)", _coordinator.Status);
        Assert.True(first.Disposed);
        Assert.False(first.HasSubscribers);

        await End(SongA);

        Assert.Equal(2, _connector.Attempts);
        Assert.NotSame(first, Obs);
        Assert.Equal(new[] { "StopRecord" }, Obs.Requests);

        await ObsReportsStopped(PathA);
        Assert.Equal((PathA, RenamedA), _host.Moves.Single());
    }

    [Fact]
    public async Task DisconnectMidLevel_AndObsStaysGone_SaysTheRecordingMayStillRun()
    {
        await Start(SongA);
        Obs.Drop("OBS closed the connection");
        _connector.Error = new ObsException("OBS is not reachable at 127.0.0.1:4455");

        await End(SongA);

        Assert.Contains(_host.Notes, n => n.Contains("could not reach OBS to stop the recording"));
        Assert.Empty(_host.Moves);
    }

    [Fact]
    public async Task DisconnectWhileWaitingForTheFile_RenamesWithThePathFromTheStopResponse()
    {
        await Start(SongA);
        await End(SongA);

        Obs.Drop("OBS closed the connection");
        await _coordinator.WhenIdleAsync();

        Assert.Equal((PathA, RenamedA), _host.Moves.Single());
        Assert.Contains("OBS disconnected before reporting the recording as finished", _host.Warnings);
        Assert.Equal("Not connected: OBS closed the connection", _coordinator.Status);
    }

    [Fact]
    public async Task ConnectionClosingTwice_IsHandledOnce()
    {
        await Start(SongA);
        await End(SongA);
        var first = Obs;

        // Hold the queue inside the next step so both close notices are queued behind it while the
        // connection is still the current one; the second is then stale when its turn comes.
        var blocked = _host.BlockOnInfo("waits until OBS has finished");
        _coordinator.LevelStarted(SongB);
        await blocked;
        first.Drop("lost");
        first.Drop("lost");
        _host.ReleaseInfo();
        await _coordinator.WhenIdleAsync();

        Assert.Single(_host.Infos, i => i == "disconnected: lost");
        Assert.Equal((PathA, RenamedA), _host.Moves.Single());
        Assert.Equal(2, _connector.Attempts);
        Assert.NotSame(first, Obs);
        Assert.False(Obs.Disposed);
        Assert.Equal(new[] { "StartRecord" }, Obs.Requests);
        Assert.Equal("Recording 'Song B' (Hard)", _coordinator.Status);
    }

    [Fact]
    public async Task StoppedEventNeverArrives_RenamesAfterTheTimeout_AndStartsTheQueuedLevel()
    {
        await Start(SongA);
        _coordinator.LevelEnded(SongA);
        _coordinator.LevelStarted(SongB);
        await _coordinator.WhenIdleAsync();
        Assert.Contains(RecordingCoordinator.StopTimeout, _host.Delays);

        Assert.Equal(1, _host.FireStopTimeout());
        await ObsTestData.WaitFor(() => _host.Moves.Count == 1, "the fallback rename");
        await _coordinator.WhenIdleAsync();

        Assert.Equal((PathA, RenamedA), _host.Moves.Single());
        Assert.Contains("OBS did not report the recording as finished within 30 s", _host.Warnings);
        Assert.Equal(new[] { "StartRecord", "StopRecord", "StartRecord" }, Obs.Requests);

        // The stopped event for that file turns up late, while the next level is being recorded:
        // it is neither renamed again nor mistaken for the new recording ending.
        await ObsReportsStopped(PathA);
        Assert.Single(_host.Moves);
        Assert.DoesNotContain(_host.Notes, n => n.Contains("before the level ended"));
        Assert.Equal("Recording 'Song B' (Hard)", _coordinator.Status);
    }

    [Fact]
    public async Task StopTimeout_IsCancelledByTheStoppedEvent()
    {
        await Start(SongA);
        await End(SongA);
        await ObsReportsStopped(PathA);

        Assert.Equal(0, _host.FireStopTimeout());
        await _coordinator.WhenIdleAsync();

        Assert.Single(_host.Moves);
        Assert.Empty(_host.Warnings);
    }

    [Fact]
    public async Task StopNotRunning_MeansStoppedInObs_NothingToRename()
    {
        _connector.Configure = c => c.Respond = type => type == "StopRecord" ? ObsTestData.Fail(type, 501) : ObsTestData.Ok(type);

        await Start(SongA);
        await End(SongA);
        await ObsReportsStopped(PathA);

        Assert.Empty(_host.Moves);
        Assert.Empty(_host.Notes);
        Assert.Contains(_host.Infos, i => i.Contains("was not recording any more"));
        Assert.Equal("Connected to OBS 31.0.0", _coordinator.Status);
    }

    [Fact]
    public async Task RecordingEndedInObsMidLevel_IsReported_AndKeepsItsName()
    {
        _connector.Configure = c => c.Respond = type => type == "StopRecord" ? ObsTestData.Fail(type, 501) : ObsTestData.Ok(type);
        await Start(SongA);

        await ObsReportsStopped(PathA);

        Assert.Single(_host.Notes);
        Assert.Contains("OBS stopped recording before the level ended", _host.Notes[0]);
        Assert.Empty(_host.Moves);

        await End(SongA);

        Assert.Equal(new[] { "StartRecord", "StopRecord" }, Obs.Requests);
        Assert.Empty(_host.Moves);
        Assert.Single(_host.Notes);
    }

    [Fact]
    public async Task StopRefused_IsReported()
    {
        _connector.Configure = c => c.Respond = type => type == "StopRecord" ? ObsTestData.Fail(type, 600) : ObsTestData.Ok(type);

        await Start(SongA);
        await End(SongA);

        Assert.Contains("OBS did not stop recording (code 600)", _host.Warnings);
        Assert.Equal("Connected to OBS 31.0.0", _coordinator.Status);
    }

    [Fact]
    public async Task StopRequestFails_IsReported()
    {
        _connector.Configure = c => c.Respond = type =>
            type == "StopRecord" ? throw new ObsException("OBS did not answer StopRecord within 5 s") : ObsTestData.Ok(type);

        await Start(SongA);
        await End(SongA);

        Assert.Contains("could not stop the recording: OBS did not answer StopRecord within 5 s", _host.Warnings);
        Assert.Empty(_host.Moves);
    }

    [Fact]
    public async Task Rename_RetriesWhileTheFileIsStillOpen()
    {
        _host.MoveError = attempt => attempt <= 2 ? new IOException("sharing violation") : null;
        await Start(SongA);
        await End(SongA);
        await ObsReportsStopped(PathA);

        Assert.Equal(3, _host.MoveAttempts);
        Assert.Equal((PathA, RenamedA), _host.Moves.Single());
        Assert.Equal(2, _host.Delays.Count(d => d == RecordingCoordinator.RenameRetryDelay));
        Assert.Empty(_host.Warnings);
    }

    [Fact]
    public async Task Rename_GivesUpAfterEnoughAttempts()
    {
        _host.MoveError = _ => new UnauthorizedAccessException("locked");
        await Start(SongA);
        await End(SongA);
        await ObsReportsStopped(PathA);

        Assert.Equal(RecordingCoordinator.RenameAttempts, _host.MoveAttempts);
        Assert.Empty(_host.Moves);
        Assert.Contains(_host.Warnings, w => w.StartsWith("could not rename " + PathA));
        Assert.Equal(new[] { "OBS recording: could not rename 2026-01-02 10-00-00.mp4" }, _host.Notes);
    }

    [Fact]
    public async Task Rename_MissingFile_IsNotRetried()
    {
        _host.MoveError = _ => new FileNotFoundException("gone");
        await Start(SongA);
        await End(SongA);
        await ObsReportsStopped(PathA);

        Assert.Equal(1, _host.MoveAttempts);
        Assert.Contains("recording not found at " + PathA + "; not renamed", _host.Warnings);
    }

    [Fact]
    public async Task Rename_UnexpectedError_IsLogged()
    {
        _host.MoveError = _ => new InvalidOperationException("odd");
        await Start(SongA);
        await End(SongA);
        await ObsReportsStopped(PathA);

        Assert.Contains("rename of " + PathA + " failed: odd", _host.Warnings);
    }

    [Fact]
    public async Task Rename_NeverOverwrites()
    {
        _host.AddFile(RenamedA);
        await Start(SongA);
        await End(SongA);
        await ObsReportsStopped(PathA);

        Assert.Equal("C:/Videos/2026-01-02 10-00-00 - Song A - Expert+ (2).mp4", _host.Moves.Single().To);
    }

    [Fact]
    public async Task Rename_NothingToAdd_LeavesTheFile()
    {
        await Start(SongA);
        await End(new RecordingLabel("???", string.Empty));
        await ObsReportsStopped(PathA);

        Assert.Empty(_host.Moves);
        Assert.Contains("saved " + PathA, _host.Infos);
    }

    [Fact]
    public async Task EndWithoutAStart_DoesNothing()
    {
        await End(SongA);

        Assert.Equal(0, _connector.Attempts);
        Assert.Empty(_host.Notes);
    }

    [Fact]
    public async Task Probe_ConnectsQuietly()
    {
        _coordinator.Probe();
        await _coordinator.WhenIdleAsync();

        Assert.Equal(1, _connector.Attempts);
        Assert.Equal("Connected to OBS 31.0.0", _coordinator.Status);
        Assert.Empty(Obs.Requests);

        _coordinator.Probe();
        await _coordinator.WhenIdleAsync();
        Assert.Equal(1, _connector.Attempts);
    }

    [Fact]
    public async Task Probe_FailureIsOnlyLogged_TheLevelStartThenSaysIt()
    {
        _connector.Error = new ObsException("OBS is not reachable at 127.0.0.1:4455");

        _coordinator.Probe();
        await _coordinator.WhenIdleAsync();

        Assert.Empty(_host.Notes);
        Assert.Empty(_host.Warnings);
        Assert.Contains("not connected: OBS is not reachable at 127.0.0.1:4455", _host.Infos);
        Assert.StartsWith("Not connected: ", _coordinator.Status);

        await Start(SongA);
        Assert.Single(_host.Notes);
    }

    [Fact]
    public async Task Disconnect_ClosesAnIdleConnection()
    {
        _coordinator.Probe();
        await _coordinator.WhenIdleAsync();
        var connection = Obs;

        _coordinator.Disconnect();
        await _coordinator.WhenIdleAsync();

        Assert.True(connection.Disposed);
        Assert.False(connection.HasSubscribers);
        Assert.Equal(RecordingCoordinator.NotConnectedYet, _coordinator.Status);
    }

    [Fact]
    public async Task Disconnect_WhileRecording_KeepsTheConnection()
    {
        await Start(SongA);

        _coordinator.Disconnect();
        await _coordinator.WhenIdleAsync();

        Assert.False(Obs.Disposed);
        await End(SongA);
        await ObsReportsStopped(PathA);
        Assert.Single(_host.Moves);
    }

    [Fact]
    public async Task Shutdown_MidLevel_StopsRenamesAndCloses()
    {
        _connector.Configure = c => c.Respond = type =>
        {
            if (type == "StopRecord")
            {
                // OBS finishes the file right away.
                c.Raise(ObsTestData.StoppedEvent(PathA));
                return ObsTestData.StopOk(PathA);
            }

            return ObsTestData.Ok(type);
        };
        await Start(SongA);
        var connection = Obs;

        await _coordinator.ShutdownAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new[] { "StartRecord", "StopRecord" }, connection.Requests);
        Assert.Equal((PathA, RenamedA), _host.Moves.Single());
        Assert.True(connection.Disposed);

        // The level scene reports its end after the app service is gone: ignored.
        _coordinator.LevelEnded(SongA);
        _coordinator.LevelStarted(SongB);
        _coordinator.Probe();
        _coordinator.Disconnect();
        await _coordinator.WhenIdleAsync();
        await _coordinator.ShutdownAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, connection.Requests.Count);
        Assert.Equal(1, _connector.Attempts);
    }

    [Fact]
    public async Task Shutdown_DoesNotWaitLongerThanItsBudget()
    {
        await Start(SongA);
        var connection = Obs;
        var clock = Stopwatch.StartNew();

        await _coordinator.ShutdownAsync(TimeSpan.FromMilliseconds(200));

        Assert.True(clock.ElapsedMilliseconds < 3000, "shutdown took " + clock.ElapsedMilliseconds + " ms");
        Assert.Contains("StopRecord", connection.Requests);
        Assert.Empty(_host.Moves);
        Assert.True(connection.Disposed);
    }

    [Fact]
    public async Task Shutdown_WhileAFileIsBeingFinished_WaitsForItAndStartsNothing()
    {
        await Start(SongA);
        _coordinator.LevelEnded(SongA);
        _coordinator.LevelStarted(SongB);
        await _coordinator.WhenIdleAsync();
        var connection = Obs;

        var shutdown = _coordinator.ShutdownAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(50, TestContext.Current.CancellationToken);
        connection.Raise(ObsTestData.StoppedEvent(PathA));
        await shutdown;

        Assert.Equal((PathA, RenamedA), _host.Moves.Single());
        Assert.Equal(new[] { "StartRecord", "StopRecord" }, connection.Requests);
        Assert.True(connection.Disposed);
    }

    [Fact]
    public async Task Shutdown_WhenIdle_IsQuick()
    {
        _coordinator.Probe();
        await _coordinator.WhenIdleAsync();
        var clock = Stopwatch.StartNew();

        await _coordinator.ShutdownAsync(TimeSpan.FromSeconds(5));

        Assert.True(clock.ElapsedMilliseconds < 2000);
        Assert.True(Obs.Disposed);
        Assert.Empty(Obs.Requests);
    }

    [Fact]
    public async Task AFailingStep_IsReported_AndTheNextStillRuns()
    {
        _coordinator.Probe();
        await _coordinator.WhenIdleAsync();

        _host.ThrowOnNextInfo = true;
        await Start(SongA);

        Assert.Contains(_host.Warnings, w => w.StartsWith("unexpected error: ") && w.Contains("log is broken"));

        await End(SongA);
        await ObsReportsStopped(PathA);
        Assert.Single(_host.Moves);
    }
}
