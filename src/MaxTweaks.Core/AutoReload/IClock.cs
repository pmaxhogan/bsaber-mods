namespace MaxTweaks.Core.AutoReload;

/// <summary>
/// Monotonic time source in seconds. Injected so the debounce logic can be tested without waiting.
/// Implementations must be safe to read from any thread (file watcher callbacks run on the pool).
/// </summary>
public interface IClock
{
    double Now { get; }
}
