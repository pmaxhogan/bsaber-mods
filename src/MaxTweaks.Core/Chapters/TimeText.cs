using System;
using System.Globalization;

namespace MaxTweaks.Core.Chapters;

/// <summary>
/// Parses and formats chapter times. Accepted text forms (invariant culture, '.' decimal point):
/// "ss", "ss.fff", "m:ss", "m:ss.fff", "h:mm:ss(.fff)". Minutes and seconds after the first field
/// must be below 60. Negative values are rejected.
/// </summary>
public static class TimeText
{
    public static bool TryParse(string? text, out float seconds)
    {
        seconds = 0f;
        if (text == null)
        {
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed.Length == 0 || trimmed[0] == '-' || trimmed[0] == '+')
        {
            return false;
        }

        var parts = trimmed.Split(':');
        if (parts.Length > 3)
        {
            return false;
        }

        double total = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            var isLast = i == parts.Length - 1;
            if (part.Length == 0 || part.IndexOf('e') >= 0 || part.IndexOf('E') >= 0)
            {
                return false;
            }

            // Only the final (seconds) field may carry a fraction.
            if (!isLast && part.IndexOf('.') >= 0)
            {
                return false;
            }

            if (!double.TryParse(part, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
            {
                return false;
            }

            if (i > 0 && value >= 60)
            {
                return false;
            }

            total = (total * 60) + value;
        }

        if (double.IsNaN(total) || double.IsInfinity(total) || total > float.MaxValue)
        {
            return false;
        }

        seconds = (float)total;
        return true;
    }

    /// <summary>Formats seconds as "m:ss.fff" (always three decimals), e.g. 75.5 -> "1:15.500".</summary>
    public static string Format(float seconds)
    {
        if (seconds < 0 || float.IsNaN(seconds) || float.IsInfinity(seconds))
        {
            return seconds.ToString(CultureInfo.InvariantCulture);
        }

        var totalMs = (long)Math.Round(seconds * 1000.0);
        var minutes = totalMs / 60000;
        var rest = totalMs % 60000;
        return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}.{2:000}", minutes, rest / 1000, rest % 1000);
    }
}
