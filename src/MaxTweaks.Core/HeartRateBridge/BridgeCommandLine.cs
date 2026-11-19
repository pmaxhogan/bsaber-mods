namespace MaxTweaks.Core.HeartRateBridge;

/// <summary>Builds the argument string that starts the heart-rate bridge script through uv.</summary>
public static class BridgeCommandLine
{
    public const string PythonVersion = "3.13";

    /// <summary>Exit code the script returns when another bridge already holds the singleton guard.</summary>
    public const int AlreadyRunningExitCode = 3;

    public static string Arguments(string scriptPath, string? extraArguments, int parentPid)
    {
        var args = "run --python " + PythonVersion + " " + Quote(scriptPath) + " --parent-pid " + parentPid;
        var extra = (extraArguments ?? string.Empty).Trim();
        return extra.Length == 0 ? args : args + " " + extra;
    }

    /// <summary>See <see cref="CommandLine.Quote"/>.</summary>
    public static string Quote(string value) => CommandLine.Quote(value);
}
