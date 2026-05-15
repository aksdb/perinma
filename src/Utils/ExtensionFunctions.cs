using System;
using NodaTime;

namespace perinma.Utils;

public static class ExtensionFunctions
{
    private static readonly DateTimeZone LocalTimeZone = DateTimeZoneProviders.Tzdb.GetSystemDefault();

    public static TResult Let<T, TResult>(this T value, Func<T, TResult> func) =>
        func(value);

    public static void Let<T>(this T value, Action<T> action) =>
        action(value);

    public static LocalDateTime ToLocalDateTime(this Instant instant) =>
        instant.InZone(LocalTimeZone).LocalDateTime;

    extension(LocalDateTime localDateTime)
    {
        public ZonedDateTime ToZonedDateTime() =>
            localDateTime.InZoneLeniently(LocalTimeZone);

        public Instant ToInstant() =>
            localDateTime.ToZonedDateTime().ToInstant();
    }
}