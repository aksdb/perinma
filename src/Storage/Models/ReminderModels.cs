using System;

namespace perinma.Storage.Models;

public class ReminderDbo
{
    public required string ReminderId { get; set; }
    public required int TargetType { get; set; }
    public required string TargetId { get; set; }
    public required long TargetTime { get; set; }
    public required long TriggerTime { get; set; }
}

public class ReminderWithEvent
{
    public required string ReminderId { get; init; }
    public required int TargetType { get; init; }
    public required string TargetId { get; init; }
    public required long TargetTime { get; init; }
    public required long TriggerTime { get; init; }
    public required string EventTitle { get; init; }
    public required string CalendarName { get; init; }
    public required string CalendarColor { get; init; }
    public required DateTime StartTime { get; init; }
}

public enum SnoozeInterval
{
    OneMinute,
    FiveMinutes,
    TenMinutes,
    FifteenMinutes,
    ThirtyMinutes,
    OneHour,
    TwoHours,
    Tomorrow,
    OneMinuteBeforeStart,
    WhenItStarts
}
