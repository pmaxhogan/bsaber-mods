using System.Threading;

namespace MaxTweaks.Common.Obs;

/// <summary>
/// Process lifetime: what the OBS features saved before the game's last internal restart. Such a
/// restart (base game settings, a mod asking for one) tears down the app container too, so the
/// features' services and their counts start over; each service adds its count here when it goes
/// and shows this plus its own, so "since the game started" stays true. Any thread.
/// </summary>
internal static class ObsSessionTotals
{
    private static int recordings;
    private static int clips;

    public static int Recordings => Volatile.Read(ref recordings);

    public static int Clips => Volatile.Read(ref clips);

    public static void AddRecordings(int count) => Interlocked.Add(ref recordings, count);

    public static void AddClips(int count) => Interlocked.Add(ref clips, count);
}
