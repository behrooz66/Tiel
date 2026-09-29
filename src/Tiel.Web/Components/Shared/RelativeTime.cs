using System.Globalization;

namespace Tiel.Web.Components.Shared;

/// <summary>Short relative times for lists: "now", "5m", "3h", "2d", then a date.</summary>
public static class RelativeTime
{
    public static string Format(DateTime utc, DateTime nowUtc)
    {
        var age = nowUtc - utc;
        if (age < TimeSpan.FromMinutes(1))
        {
            return "now";
        }

        if (age < TimeSpan.FromHours(1))
        {
            return $"{(int)age.TotalMinutes}m";
        }

        if (age < TimeSpan.FromDays(1))
        {
            return $"{(int)age.TotalHours}h";
        }

        if (age < TimeSpan.FromDays(7))
        {
            return $"{(int)age.TotalDays}d";
        }

        var local = utc.ToLocalTime();
        return local.Year == nowUtc.ToLocalTime().Year
            ? local.ToString("MMM d", CultureInfo.CurrentCulture)
            : local.ToString("MMM d, yyyy", CultureInfo.CurrentCulture);
    }
}
