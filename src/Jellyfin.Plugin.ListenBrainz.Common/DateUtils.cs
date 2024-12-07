using System.Globalization;

namespace Jellyfin.Plugin.ListenBrainz.Common;

/// <summary>
/// DateTime utilities.
/// </summary>
public static class DateUtils
{
    /// <summary>
    /// Gets get UNIX timestamp of <see cref="DateTime.Now"/>.
    /// </summary>
    public static long CurrentTimestamp => new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds();

    /// <summary>
    /// Gets a today's date in ISO format (yyyy-MM-dd).
    /// </summary>
    public static string TodayIso => DateTime.Today.ToString("yyyy-MM-dd", DateTimeFormatInfo.InvariantInfo);
}
