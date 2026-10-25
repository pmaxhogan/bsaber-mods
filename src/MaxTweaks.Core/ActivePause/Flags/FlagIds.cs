using System;
using System.Globalization;

namespace MaxTweaks.Core.ActivePause.Flags;

/// <summary>Flag ids: <c>yyyyMMdd-HHmmss-xxxx</c> (UTC timestamp and 4 lowercase hex digits).</summary>
public static class FlagIds
{
    public static string New(DateTime utc, Random random)
    {
        if (random == null)
        {
            throw new ArgumentNullException(nameof(random));
        }

        var stamp = utc.ToUniversalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        return stamp + "-" + random.Next(0x10000).ToString("x4", CultureInfo.InvariantCulture);
    }
}
