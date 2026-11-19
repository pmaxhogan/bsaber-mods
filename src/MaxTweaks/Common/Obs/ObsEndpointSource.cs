using System;
using System.IO;
using MaxTweaks.Configuration;
using MaxTweaks.Core.ObsRecording;

namespace MaxTweaks.Common.Obs;

/// <summary>
/// Where OBS is, for every feature that talks to it (OBS recording, OBS clips): host, port and
/// password from the ObsRecording config section when set, else from OBS's own WebSocket settings
/// file on this PC. That file is only read, and only used (ObsEndpoint.Resolve enforces it too),
/// when the host is this PC. Safe to call on any thread; called for every connection attempt, so a
/// changed setting applies to the next one.
/// </summary>
internal static class ObsEndpointSource
{
    public static ObsEndpoint Resolve(ObsRecordingConfig config, string feature)
    {
        var host = config.Host;
        ObsServerSettings? obs = null;
        if (ObsEndpoint.IsThisPcHost(string.IsNullOrWhiteSpace(host) ? ObsEndpoint.DefaultHost : host))
        {
            try
            {
                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "obs-studio",
                    "plugin_config",
                    "obs-websocket",
                    "config.json");
                if (File.Exists(path))
                {
                    obs = ObsServerSettings.Parse(File.ReadAllText(path));
                }
            }
            catch (Exception e)
            {
                Plugin.Log.Debug($"[{feature}] could not read OBS's WebSocket settings: {e.Message}");
            }
        }

        return ObsEndpoint.Resolve(host, config.Port, config.Password, obs);
    }
}
