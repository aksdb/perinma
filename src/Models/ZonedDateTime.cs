using System;

namespace perinma.Models;

/// <summary>
/// Represents a DateTime with timezone information.
/// </summary>
public readonly struct ZonedDateTime(DateTime dateTime, TimeZoneInfo timeZone) : IEquatable<ZonedDateTime>
{
    public DateTime DateTime { get; } = dateTime;
    public TimeZoneInfo TimeZone { get; } = timeZone ?? throw new ArgumentNullException(nameof(timeZone));

    public DateTimeOffset ToDateTimeOffset() => new(DateTime, TimeZone.GetUtcOffset(DateTime));

    public ZonedDateTime ToUtc()
    {
        var utcDateTime = TimeZoneInfo.ConvertTimeToUtc(DateTime, TimeZone);
        return new ZonedDateTime(utcDateTime, TimeZoneInfo.Utc);
    }

    public ZonedDateTime ConvertTo(TimeZoneInfo targetTimeZone)
    {
        var convertedDateTime = TimeZoneInfo.ConvertTime(DateTime, TimeZone, targetTimeZone);
        return new ZonedDateTime(convertedDateTime, targetTimeZone);
    }

    public override bool Equals(object? obj) => obj is ZonedDateTime other && Equals(other);

    public bool Equals(ZonedDateTime other) => DateTime.Equals(other.DateTime) && (TimeZone == null || TimeZone.Equals(other.TimeZone));

    public override int GetHashCode() => HashCode.Combine(DateTime, TimeZone);

    public static bool operator ==(ZonedDateTime left, ZonedDateTime right) => left.Equals(right);

    public static bool operator !=(ZonedDateTime left, ZonedDateTime right) => !(left == right);
    
    public static bool operator >(ZonedDateTime left, ZonedDateTime right) => left.DateTime > right.DateTime;
    public static bool operator <(ZonedDateTime left, ZonedDateTime right) => left.DateTime < right.DateTime;

    public static bool operator <=(ZonedDateTime left, ZonedDateTime right) => left.DateTime <= right.DateTime;
    public static bool operator >=(ZonedDateTime left, ZonedDateTime right) => left.DateTime >= right.DateTime;
    
    public static TimeSpan operator -(ZonedDateTime left, ZonedDateTime right) => right.DateTime - left.DateTime;
    
    public ZonedDateTime Add(TimeSpan duration)
    {
        return new ZonedDateTime(DateTime.Add(duration), TimeZone);
    }
    
    public ZonedDateTime AddMinutes(double value)
    {
        return new ZonedDateTime(DateTime.AddMinutes(value), TimeZone);
    }

    public DateTime Date => DateTime.Date;
    public TimeSpan TimeOfDay => DateTime.TimeOfDay;
    public int Hour => DateTime.Hour;
    public int Minute => DateTime.Minute;

    public string ToString(string format) => DateTime.ToString(format);
}
