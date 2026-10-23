namespace MaxTweaks.Common.Toasts;

/// <summary>
/// Small, unobtrusive one-line text toast for the menu and for gameplay.
///
/// API (resolve IToastService from any container: it is bound in the App container by
/// ToastInstaller, so menu and gameplay installers can inject it directly):
///
///   [Inject] IToastService toasts;
///   toasts.Show("Switch failed: Expert+ not available");       // about 4 s, then fades out
///   toasts.Show("Map updated on disk: Foo", seconds: 6f);      // custom hold time
///   toasts.Hide();                                             // fade out now
///   toasts.Prewarm();                                          // optional, from a menu Initialize
///
/// Behaviour:
/// - Safe to call from any thread; off-main-thread calls are marshalled to Unity's main thread.
/// - One toast at a time: a new Show replaces the current text and restarts the timer, so the
///   latest message always wins (no queue that could lag behind gameplay).
/// - Placement is chosen when shown, from the current head (main camera) yaw. In the menu: about
///   2.3 m ahead, low (0.8 m above the floor), below the main screens. In gameplay (GameCore scene):
///   about 3.5 m ahead and 2.6 m high, above the note lanes, so it never covers incoming notes.
///   It does not follow the head while visible.
/// - No raycast target: it never blocks the UI pointer. Survives scene changes (DontDestroyOnLoad)
///   and is destroyed when the app container is disposed.
/// - Rich text is off: the message is shown literally (map names can contain '&lt;').
/// </summary>
internal interface IToastService
{
    void Show(string message, float seconds = ToastService.DefaultSeconds);

    void Hide();

    /// <summary>
    /// Optional: builds the toast objects now. Call from a menu Initialize so the first toast in
    /// gameplay does not depend on finding the game's UI font there. Main thread; no-op otherwise.
    /// </summary>
    void Prewarm();
}
