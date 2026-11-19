using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaxTweaks.Core.ObsClips;
using MaxTweaks.Core.ObsRecording;
using MaxTweaks.Core.Tests.ObsRecording;
using Newtonsoft.Json.Linq;

namespace MaxTweaks.Core.Tests.ObsClips;

/// <summary>The replay-buffer side of a pretend OBS: answers for a <see cref="FakeConnection"/>.</summary>
internal sealed class ReplayObs
{
    private readonly object _gate = new();
    private readonly List<string> _parameters = new();

    /// <summary>The replay buffer output is running.</summary>
    public bool Active { get; set; }

    /// <summary>False: "Enable Replay Buffer" is off in OBS's settings (every replay request answers 604).</summary>
    public bool Available { get; set; } = true;

    public string? Mode { get; set; } = "Advanced";

    public string? AdvancedSeconds { get; set; } = "60";

    public string? SimpleSeconds { get; set; } = "20";

    /// <summary>Overrides the answer to StartReplayBuffer / SaveReplayBuffer when not 100.</summary>
    public int StartCode { get; set; } = 100;

    public int SaveCode { get; set; } = 100;

    public Exception? SaveError { get; set; }

    /// <summary>Thrown by GetReplayBufferStatus / StopReplayBuffer when set.</summary>
    public Exception? StatusError { get; set; }

    public Exception? StopError { get; set; }

    /// <summary>"Category/Name" of every profile parameter that was asked for.</summary>
    public IReadOnlyList<string> Parameters
    {
        get
        {
            lock (_gate)
            {
                return _parameters.ToArray();
            }
        }
    }

    public static ObsEvent Saved(string? path) => new("ReplayBufferSaved", new JObject { ["savedReplayPath"] = path });

    public static ObsEvent State(bool started) => new(
        "ReplayBufferStateChanged",
        new JObject { ["outputActive"] = started, ["outputState"] = started ? "OBS_WEBSOCKET_OUTPUT_STARTED" : "OBS_WEBSOCKET_OUTPUT_STOPPED" });

    public void Attach(FakeConnection connection) => connection.RespondWithData = Respond;

    private ObsResponse Respond(string type, JObject? data)
    {
        lock (_gate)
        {
            if (type != "GetProfileParameter" && !Available)
            {
                return ObsTestData.Fail(type, 604, "Replay buffer is not available.");
            }

            switch (type)
            {
                case "GetReplayBufferStatus":
                    if (StatusError != null)
                    {
                        throw StatusError;
                    }

                    return ObsTestData.Ok(type, new JObject { ["outputActive"] = Active });
                case "GetProfileParameter":
                    var key = (string?)data?["parameterCategory"] + "/" + (string?)data?["parameterName"];
                    _parameters.Add(key);
                    var value = key switch
                    {
                        "Output/Mode" => Mode,
                        "AdvOut/RecRBTime" => AdvancedSeconds,
                        "SimpleOutput/RecRBTime" => SimpleSeconds,
                        _ => null,
                    };
                    return ObsTestData.Ok(type, new JObject { ["parameterValue"] = value, ["defaultParameterValue"] = null });
                case "StartReplayBuffer":
                    if (StartCode != 100)
                    {
                        return ObsTestData.Fail(type, StartCode);
                    }

                    if (Active)
                    {
                        return ObsTestData.Fail(type, 500);
                    }

                    Active = true;
                    return ObsTestData.Ok(type);
                case "StopReplayBuffer":
                    if (StopError != null)
                    {
                        throw StopError;
                    }

                    if (!Active)
                    {
                        return ObsTestData.Fail(type, 501);
                    }

                    Active = false;
                    return ObsTestData.Ok(type);
                case "SaveReplayBuffer":
                    if (SaveError != null)
                    {
                        throw SaveError;
                    }

                    return !Active ? ObsTestData.Fail(type, 501) : SaveCode == 100 ? ObsTestData.Ok(type) : ObsTestData.Fail(type, SaveCode, "nope");
                default:
                    return ObsTestData.Fail(type, 204);
            }
        }
    }
}

/// <summary>Records what the clip coordinator tells the outside world, with an in-memory "disk" and a scripted ffmpeg.</summary>
internal sealed class FakeClipHost : IClipHost
{
    private readonly object _gate = new();
    private readonly List<string> _infos = new();
    private readonly List<string> _warnings = new();
    private readonly List<string> _notes = new();
    private readonly List<(string From, string To)> _moves = new();
    private readonly List<string> _deleted = new();
    private readonly List<(string Source, string Target, double FromEnd, double Seconds)> _cuts = new();
    private readonly List<TaskCompletionSource<bool>> _heldTimeouts = new();
    private readonly HashSet<string> _files = new();
    private double _now;
    private int _refusals;

    public double Now
    {
        get
        {
            lock (_gate)
            {
                return _now;
            }
        }

        set
        {
            lock (_gate)
            {
                _now = value;
            }
        }
    }

    public bool KeepFullReplay { get; set; }

    /// <summary>Called with the attempt number (1-based, over all cuts); null = the cut works.</summary>
    public Func<int, ClipCutResult?>? CutResult { get; set; }

    /// <summary>Called with the attempt number (1-based); a returned exception is thrown by DeleteFile.</summary>
    public Func<int, Exception?>? DeleteError { get; set; }

    /// <summary>The same for MoveFile.</summary>
    public Func<int, Exception?>? MoveError { get; set; }

    public int StatusChanges { get; private set; }

    public bool ThrowOnStatusChanged { get; set; }

    /// <summary>Thrown by CutAsync when set (a host that breaks its promise never to throw).</summary>
    public Exception? CutError { get; set; }

    public int DeleteAttempts { get; private set; }

    public int MoveAttempts { get; private set; }

    public int Refusals => Volatile.Read(ref _refusals);

    public IReadOnlyList<string> Infos => Copy(_infos);

    public IReadOnlyList<string> Warnings => Copy(_warnings);

    public IReadOnlyList<string> Notes => Copy(_notes);

    public IReadOnlyList<(string From, string To)> Moves => Copy(_moves);

    public IReadOnlyList<string> Deleted => Copy(_deleted);

    public IReadOnlyList<(string Source, string Target, double FromEnd, double Seconds)> Cuts => Copy(_cuts);

    public IReadOnlyList<string> Files
    {
        get
        {
            lock (_gate)
            {
                return _files.OrderBy(f => f, StringComparer.Ordinal).ToArray();
            }
        }
    }

    public void AddFile(string path)
    {
        lock (_gate)
        {
            _files.Add(path);
        }
    }

    public void Info(string message) => Add(_infos, message);

    public void Warn(string message) => Add(_warnings, message);

    public void Notify(string message) => Add(_notes, message);

    public void StatusChanged()
    {
        lock (_gate)
        {
            StatusChanges++;
        }

        if (ThrowOnStatusChanged)
        {
            throw new InvalidOperationException("status line is broken");
        }
    }

    public void SaveRefused() => Interlocked.Increment(ref _refusals);

    public bool FileExists(string path)
    {
        lock (_gate)
        {
            return _files.Contains(path);
        }
    }

    public void MoveFile(string from, string to)
    {
        lock (_gate)
        {
            MoveAttempts++;
            var error = MoveError?.Invoke(MoveAttempts);
            if (error != null)
            {
                throw error;
            }

            if (!_files.Remove(from))
            {
                throw new FileNotFoundException("not there", from);
            }

            _moves.Add((from, to));
            _files.Add(to);
        }
    }

    public void DeleteFile(string path)
    {
        lock (_gate)
        {
            DeleteAttempts++;
            var error = DeleteError?.Invoke(DeleteAttempts);
            if (error != null)
            {
                throw error;
            }

            _files.Remove(path);
            _deleted.Add(path);
        }
    }

    public Task<ClipCutResult> CutAsync(string source, string target, double secondsFromEnd, double seconds)
    {
        lock (_gate)
        {
            _cuts.Add((source, target, secondsFromEnd, seconds));
            if (CutError != null)
            {
                return Task.FromException<ClipCutResult>(CutError);
            }

            var result = CutResult?.Invoke(_cuts.Count) ?? ClipCutResult.Done;
            if (result.Outcome == ClipCutOutcome.Ok)
            {
                _files.Add(target);
            }

            return Task.FromResult(result);
        }
    }

    /// <summary>Short waits (retries) pass at once; the save timeout waits for <see cref="FireSaveTimeout"/>.</summary>
    public Task Delay(TimeSpan time, CancellationToken cancellation)
    {
        lock (_gate)
        {
            if (time < ClipCoordinator.SaveTimeout)
            {
                return Task.CompletedTask;
            }

            var held = new TaskCompletionSource<bool>();
            cancellation.Register(() => held.TrySetCanceled());
            _heldTimeouts.Add(held);
            return held.Task;
        }
    }

    /// <summary>Lets every pending save timeout elapse; returns how many were still pending.</summary>
    public int FireSaveTimeout()
    {
        TaskCompletionSource<bool>[] held;
        lock (_gate)
        {
            held = _heldTimeouts.ToArray();
            _heldTimeouts.Clear();
        }

        return held.Count(h => h.TrySetResult(true));
    }

    private void Add(List<string> list, string message)
    {
        lock (_gate)
        {
            list.Add(message);
        }
    }

    private IReadOnlyList<T> Copy<T>(List<T> list)
    {
        lock (_gate)
        {
            return list.ToArray();
        }
    }
}
