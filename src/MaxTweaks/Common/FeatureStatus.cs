using System.Collections.Generic;

namespace MaxTweaks.Common;

/// <summary>
/// Tracks features that disabled themselves at runtime (e.g. a game patch target was missing),
/// so the settings page can show why a feature is not working instead of failing silently.
/// </summary>
internal class FeatureStatus
{
    private readonly Dictionary<string, string> _failures = new();

    public IReadOnlyDictionary<string, string> Failures => _failures;

    public bool IsFailed(string feature) => _failures.ContainsKey(feature);

    public void MarkFailed(string feature, string reason)
    {
        _failures[feature] = reason;
        Plugin.Log.Error($"[{feature}] disabled: {reason}");
    }
}
