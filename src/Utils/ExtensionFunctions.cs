using System;
using NodaTime;

namespace perinma.Utils;

public static class ExtensionFunctions
{
    private static readonly DateTimeZone LocalTimeZone = DateTimeZoneProviders.Tzdb.GetSystemDefault();
    
    public static TResult Let<T, TResult>(this T value, Func<T, TResult> func) =>
        func(value);
    
    public static LocalDateTime ToLocalDateTime(this Instant instant) =>
        instant.InZone(LocalTimeZone).LocalDateTime;

    public static Instant ToInstant(this LocalDateTime localDateTime) =>
        localDateTime.InUtc().ToInstant();
    
    public static ZonedDateTime ToZonedDateTime(this LocalDateTime localDateTime) =>
        localDateTime.InZoneLeniently(LocalTimeZone);
}
