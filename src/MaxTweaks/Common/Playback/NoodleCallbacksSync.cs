using System;
using System.Reflection;
using Zenject;

namespace MaxTweaks.Common.Playback;

/// <summary>
/// Noodle Extensions (optional, no hard reference) spawns objects of Noodle maps from its own
/// NoodleObjectsCallbacksManager instead of the game's callbacks: a single CallbacksInTime cursor
/// over its own spawn-time-sorted list, a _prevSongtime guard (ManualUpdate returns early while
/// songTime &lt;= _prevSongtime) and its own readonly _startFilterTime. After a seek we do what
/// PracticePlugin 9.x does (SongSeekBeatmapHandler): null the cursor so it walks from its first node
/// again, lower the guard, and move the filter to the objects-from time so past objects are skipped.
/// Resolved once per player container; does nothing on non-Noodle maps (the manager is not bound).
/// </summary>
internal sealed class NoodleCallbacksSync
{
    private const string ManagerTypeName = "NoodleExtensions.Managers.NoodleObjectsCallbacksManager, NoodleExtensions";

    private readonly DiContainer _container;
    private bool _looked;
    private object? _manager;
    private FieldInfo? _callbacksInTime;
    private FieldInfo? _prevSongtime;
    private FieldInfo? _startFilterTime;

    public NoodleCallbacksSync(DiContainer container)
    {
        _container = container;
    }

    /// <summary>True when Noodle's object callbacks manager runs in this container.</summary>
    public bool IsActive => Resolve();

    public void Resync(float objectsFromTime)
    {
        if (!Resolve())
        {
            return;
        }

        if (_callbacksInTime!.GetValue(_manager) is CallbacksInTime callbacks)
        {
            callbacks.lastProcessedNode = null;
        }

        _prevSongtime!.SetValue(_manager, float.MinValue);
        _startFilterTime!.SetValue(_manager, objectsFromTime);
    }

    private bool Resolve()
    {
        if (!_looked)
        {
            _looked = true;
            var type = Type.GetType(ManagerTypeName, throwOnError: false);
            if (type != null)
            {
                const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                _callbacksInTime = type.GetField("_callbacksInTime", Flags);
                _prevSongtime = type.GetField("_prevSongtime", Flags);
                _startFilterTime = type.GetField("_startFilterTime", Flags);
                _manager = _container.TryResolve(type);
                if (_manager != null && (_callbacksInTime == null || _prevSongtime == null || _startFilterTime == null))
                {
                    Plugin.Log.Warn("[Playback] Noodle Extensions callbacks manager found but its fields changed; Noodle objects will not follow seeks");
                    _manager = null;
                }
            }
        }

        return _manager != null;
    }
}
