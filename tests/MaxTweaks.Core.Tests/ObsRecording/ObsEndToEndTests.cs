using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaxTweaks.Core.ObsRecording;
using Xunit;

namespace MaxTweaks.Core.Tests.ObsRecording;

/// <summary>
/// The whole client (real ClientWebSocket, connector, connection, coordinator, real files) against
/// <see cref="FakeObsServer"/> on a local port.
/// </summary>
public sealed class ObsEndToEndTests : IDisposable
{
    private const string Password = "made-up-test-password";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "maxtweaks-obs-" + Guid.NewGuid().ToString("N"));
    private readonly FakeObsServer _server;
    private readonly DiskHost _host = new();

    public ObsEndToEndTests()
    {
        Directory.CreateDirectory(_folder);
        _server = new FakeObsServer(_folder, Password);
    }

    public void Dispose()
    {
        _server.Dispose();
        try
        {
            Directory.Delete(_folder, true);
        }
        catch (IOException)
        {
            // A handle may linger for a moment; the temp folder is cleaned by the OS eventually.
        }
    }

    private ObsConnector Connector(string password = Password) =>
        new(() => new ObsEndpoint("127.0.0.1", _server.Port, password, false), () => new ClientWebSocketObsSocket());

    private RecordingCoordinator Coordinator(string password = Password) => new(Connector(password), _host);

    private string[] Files() => Directory.GetFiles(_folder).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray()!;

    [Fact]
    public async Task Level_IsRecordedAndRenamed()
    {
        var coordinator = Coordinator();

        coordinator.LevelStarted(new RecordingLabel("Song: One?", "Expert+"));
        await coordinator.WhenIdleAsync();

        Assert.True(_server.OutputActive);
        Assert.Equal(new[] { "2026-01-02 10-01-00.mp4" }, Files());
        Assert.StartsWith("Recording ", coordinator.Status);

        coordinator.LevelEnded(new RecordingLabel("Song: One?", "Expert+"));
        await ObsTestData.WaitFor(() => _host.Notes.Any(n => n.StartsWith("Recording saved: ")), "the rename");
        await coordinator.WhenIdleAsync();

        Assert.False(_server.OutputActive);
        Assert.Equal(new[] { "2026-01-02 10-01-00 - Song One - Expert+.mp4" }, Files());
        Assert.Equal(new[] { "StartRecord", "StopRecord" }, _server.Requests);
        Assert.Equal("Connected to OBS 31.0.0", coordinator.Status);
        Assert.Empty(_host.Warnings);

        await coordinator.ShutdownAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Restart_RecordsTwoFiles_TheSecondStartWaitsForTheFirstFile()
    {
        _server.FinishDelay = TimeSpan.FromMilliseconds(300);
        var coordinator = Coordinator();
        var first = new RecordingLabel("First", "Hard");
        var second = new RecordingLabel("Second", "OneSaber Expert");

        coordinator.LevelStarted(first);
        coordinator.LevelEnded(first);
        coordinator.LevelStarted(second);
        await ObsTestData.WaitFor(() => _server.RecordingsStarted == 2, "the second recording");
        await coordinator.WhenIdleAsync();

        // A StartRecord sent before the first output had stopped would have been refused with 500.
        Assert.Equal(new[] { "StartRecord", "StopRecord", "StartRecord" }, _server.Requests);
        Assert.True(_server.OutputActive);

        coordinator.LevelEnded(second);
        await ObsTestData.WaitFor(() => _host.Notes.Count(n => n.StartsWith("Recording saved: ")) == 2, "both renames");

        Assert.Equal(
            new[] { "2026-01-02 10-01-00 - First - Hard.mp4", "2026-01-02 10-02-00 - Second - OneSaber Expert.mp4" },
            Files());
        Assert.Empty(_host.Warnings);
        await coordinator.ShutdownAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ARecordingThePlayerStarted_IsNeverStoppedOrRenamed()
    {
        _server.StartOwnRecording();
        var coordinator = Coordinator();
        var label = new RecordingLabel("Song", "Easy");

        coordinator.LevelStarted(label);
        await coordinator.WhenIdleAsync();
        coordinator.LevelEnded(label);
        await coordinator.WhenIdleAsync();

        Assert.Equal(new[] { "StartRecord" }, _server.Requests);
        Assert.True(_server.OutputActive);
        Assert.Equal(new[] { "2026-01-02 10-01-00.mp4" }, Files());
        Assert.Single(_host.Notes);
        Assert.Contains("already recording", _host.Notes[0]);
        await coordinator.ShutdownAsync(TimeSpan.FromSeconds(2));
        Assert.True(_server.OutputActive);
    }

    [Fact]
    public async Task FileStillLockedAfterObsReportsIt_IsRenamedOnceReleased()
    {
        _server.HoldAfterStopped = TimeSpan.FromMilliseconds(700);
        var coordinator = Coordinator();
        var label = new RecordingLabel("Locked", "Normal");

        coordinator.LevelStarted(label);
        coordinator.LevelEnded(label);
        await ObsTestData.WaitFor(() => _host.Notes.Any(n => n.StartsWith("Recording saved: ")), "the rename");

        Assert.Equal(new[] { "2026-01-02 10-01-00 - Locked - Normal.mp4" }, Files());
        await coordinator.ShutdownAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task QuitMidLevel_StopsAndRenamesWithinTheBudget()
    {
        var coordinator = Coordinator();
        coordinator.LevelStarted(new RecordingLabel("Quit", "Hard"));
        await coordinator.WhenIdleAsync();

        await coordinator.ShutdownAsync(TimeSpan.FromSeconds(5));

        Assert.False(_server.OutputActive);
        Assert.Equal(new[] { "2026-01-02 10-01-00 - Quit - Hard.mp4" }, Files());
    }

    [Fact]
    public async Task WrongPassword_IsReported_AndNothingIsRecorded()
    {
        var coordinator = Coordinator("not-the-password");

        coordinator.LevelStarted(new RecordingLabel("Song", "Easy"));
        await coordinator.WhenIdleAsync();

        Assert.Contains("rejected the WebSocket password", coordinator.Status);
        Assert.Single(_host.Notes);
        Assert.DoesNotContain("not-the-password", _host.Notes[0]);
        Assert.False(_server.OutputActive);
        Assert.Empty(_server.Requests);
    }

    [Fact]
    public async Task Connection_HandlesMessagesLargerThanItsBuffer_AndErrorCodes()
    {
        using var connection = await Connector().ConnectAsync(CancellationToken.None);
        connection.Start();

        var version = await connection.RequestAsync(ObsProtocol.GetVersion, CancellationToken.None);
        var status = await connection.RequestAsync(ObsProtocol.GetRecordStatus, CancellationToken.None);
        var stop = await connection.RequestAsync(ObsProtocol.StopRecord, CancellationToken.None);

        Assert.Equal("31.0.0", version.GetString("obsVersion"));
        Assert.Equal(20000, version.GetString("padding")!.Length);
        Assert.False(status.GetBool(ObsProtocol.OutputActive));
        Assert.False(stop.Ok);
        Assert.Equal(ObsProtocol.StatusOutputNotRunning, stop.Code);
        Assert.Equal("31.0.0", connection.ObsVersion);
    }

    [Fact]
    public async Task ServerGoingAway_ClosesTheConnection()
    {
        using var connection = await Connector().ConnectAsync(CancellationToken.None);
        var closed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Closed += (_, reason) => closed.TrySetResult(reason);
        connection.Start();

        _server.Dispose();

        var first = await Task.WhenAny(closed.Task, Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.Same(closed.Task, first);
        Assert.False(connection.IsOpen);
    }

    /// <summary>The real thing: files on disk, real waiting; keeps what was logged.</summary>
    private sealed class DiskHost : IRecordingHost
    {
        private readonly List<string> _notes = new();
        private readonly List<string> _warnings = new();

        public IReadOnlyList<string> Notes
        {
            get
            {
                lock (_notes)
                {
                    return _notes.ToArray();
                }
            }
        }

        public IReadOnlyList<string> Warnings
        {
            get
            {
                lock (_warnings)
                {
                    return _warnings.ToArray();
                }
            }
        }

        public void Info(string message)
        {
        }

        public void Warn(string message)
        {
            lock (_warnings)
            {
                _warnings.Add(message);
            }
        }

        public void Notify(string message)
        {
            lock (_notes)
            {
                _notes.Add(message);
            }
        }

        public void StatusChanged()
        {
        }

        public bool FileExists(string path) => File.Exists(path);

        public void MoveFile(string from, string to) => File.Move(from, to);

        public Task Delay(TimeSpan time, CancellationToken cancellation) => Task.Delay(time, cancellation);
    }
}
