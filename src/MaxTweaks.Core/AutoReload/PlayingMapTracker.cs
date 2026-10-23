namespace MaxTweaks.Core.AutoReload;

/// <summary>
/// During gameplay: remembers which map folder is being played and reports, once per play, that it
/// changed on disk.
/// </summary>
public sealed class PlayingMapTracker
{
    private string? _playingFolder;
    private bool _notified;

    public string? PlayingFolder => _playingFolder;

    /// <summary>Starts tracking a new play. Null for levels without a folder (OST/DLC).</summary>
    public void Start(string? folderKey)
    {
        _playingFolder = folderKey;
        _notified = false;
    }

    public void Stop()
    {
        _playingFolder = null;
        _notified = false;
    }

    /// <summary>True the first time a batch contains the playing map's folder; false afterwards.</summary>
    public bool ShouldNotify(ChangeBatch? batch)
    {
        if (_notified || _playingFolder == null || batch == null || !batch.Contains(_playingFolder))
        {
            return false;
        }

        _notified = true;
        return true;
    }
}
