using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using MaxTweaks.Core.ObsClips;

namespace MaxTweaks.Features.ObsClips;

/// <summary>
/// Runs ffmpeg once to cut a clip out of a saved replay (arguments: Core <see cref="ClipCut"/>).
/// A stream copy of a few seconds: it finishes in well under a second. Runs on the thread pool at
/// below-normal priority, so the game never waits for it and it never competes with the game.
/// </summary>
internal static class FfmpegCutter
{
    private const int TimeoutMilliseconds = 60000;
    private const int MaxErrorLength = 300;

    public static Task<ClipCutResult> CutAsync(string? ffmpegPath, string source, string target, double secondsFromEnd, double seconds) =>
        Task.Run(() => Cut(ffmpegPath, source, target, secondsFromEnd, seconds));

    private static ClipCutResult Cut(string? ffmpegPath, string source, string target, double secondsFromEnd, double seconds)
    {
        var ffmpeg = string.IsNullOrWhiteSpace(ffmpegPath) ? "ffmpeg" : ffmpegPath!.Trim();
        try
        {
            var info = new ProcessStartInfo(ffmpeg, ClipCut.Arguments(source, target, secondsFromEnd, seconds))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };

            var errors = new StringBuilder();
            using var process = new Process { StartInfo = info };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    lock (errors)
                    {
                        if (errors.Length < MaxErrorLength)
                        {
                            errors.Append(e.Data).Append(' ');
                        }
                    }
                }
            };

            try
            {
                process.Start();
            }
            catch (Exception e) when (e is Win32Exception || e is FileNotFoundException)
            {
                return new ClipCutResult(ClipCutOutcome.ToolMissing, $"cannot start '{ffmpeg}': {e.Message}");
            }

            try
            {
                process.PriorityClass = ProcessPriorityClass.BelowNormal;
            }
            catch (Exception)
            {
                // Already gone, or not allowed: the priority is a courtesy.
            }

            process.BeginErrorReadLine();
            if (!process.WaitForExit(TimeoutMilliseconds))
            {
                try
                {
                    process.Kill();
                }
                catch (Exception)
                {
                    // It ended by itself in the meantime.
                }

                Remove(target);
                return new ClipCutResult(ClipCutOutcome.Failed, "ffmpeg did not finish within 60 s");
            }

            // Lets the error reader finish before the text is used.
            process.WaitForExit();
            if (process.ExitCode == 0 && File.Exists(target) && new FileInfo(target).Length > 0)
            {
                return ClipCutResult.Done;
            }

            Remove(target);
            string said;
            lock (errors)
            {
                said = errors.ToString().Trim();
            }

            return new ClipCutResult(ClipCutOutcome.Failed, $"ffmpeg exited with {process.ExitCode}" + (said.Length > 0 ? ": " + said : string.Empty));
        }
        catch (Exception e)
        {
            Remove(target);
            return new ClipCutResult(ClipCutOutcome.Failed, e.GetType().Name + ": " + e.Message);
        }
    }

    /// <summary>A failed cut must not leave half a clip behind.</summary>
    private static void Remove(string target)
    {
        try
        {
            if (File.Exists(target))
            {
                File.Delete(target);
            }
        }
        catch (Exception)
        {
            // Nothing more to do about it here.
        }
    }
}
