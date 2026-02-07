using System;

namespace perinma.Models;

public struct TimeRange(ZonedDateTime start, ZonedDateTime end)
{
    /// <summary>
    /// Construct an infinite time range, spanning as much as DateTime can offer.
    /// </summary>
    public static TimeRange Open() => new(new ZonedDateTime(DateTime.MinValue, TimeZoneInfo.Utc),
        new ZonedDateTime(DateTime.MaxValue, TimeZoneInfo.Utc));

    /// <summary>
    /// Construct an open time range, spanning from the given start until "infinity".
    /// </summary>
    /// <param name="start">The start of the open timespan</param>
    public static TimeRange From(ZonedDateTime start) =>
        new(start, new ZonedDateTime(DateTime.MaxValue, TimeZoneInfo.Utc));

    /// <summary>
    /// Construct a limited time range that starts at the earliest possible time.
    /// </summary>
    /// <param name="end">The time until this range reaches.</param>
    public static TimeRange Until(ZonedDateTime end) =>
        new(new ZonedDateTime(DateTime.MinValue, TimeZoneInfo.Utc), end);
}
