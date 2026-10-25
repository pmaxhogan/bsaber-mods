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

    /// <summary>Windows command-line quoting for one argument (backslashes before a quote are doubled).</summary>
    public static string Quote(string value)
    {
        if (value.Length > 0 && value.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
        {
            return value;
        }

        var sb = new System.Text.StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in value)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                sb.Append('\\', backslashes * 2 + 1).Append('"');
            }
            else
            {
                sb.Append('\\', backslashes).Append(c);
            }

            backslashes = 0;
        }

        return sb.Append('\\', backslashes * 2).Append('"').ToString();
    }
}
