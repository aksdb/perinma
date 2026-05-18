using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NodaTime;

namespace perinma.Models;

/// <summary>
/// Represents the user's response status to an event invitation.
/// </summary>
public enum EventResponseStatus
{
    /// <summary>No response status available (not an invitation or unknown).</summary>
    None,

    /// <summary>User has not responded to the invitation yet.</summary>
    NeedsAction,

    /// <summary>User has declined the invitation.</summary>
    Declined,

    /// <summary>User has tentatively accepted the invitation.</summary>
    Tentative,

    /// <summary>User has accepted the invitation.</summary>
    Accepted
}

public record EventReference
{
    public required Calendar Calendar { get; init; }
    public required Guid Id { get; init; }
    public string? ExternalId { get; init; }
}

public enum RecurringEventAction
{
    EditOccurrence,
    EditSeries,
    DeleteOccurrence,
    DeleteSeries,
    RevertOverride,
}

public enum RecurrenceEditKind
{
    None,
    SeriesMaster,
    GeneratedOccurrence,
    OverrideOccurrence,
}

public sealed record RecurrenceEditInfo
{
    public required RecurrenceEditKind Kind { get; init; }
    public string? SeriesExternalId { get; init; }
    public Instant? OriginalStartTime { get; init; }
    public string? BackingExternalId { get; init; }
    public HashSet<RecurringEventAction> AllowedActions { get; init; } = [];
}

public enum RecurrenceFrequency
{
    Daily,
    Weekly,
    Monthly,
    Yearly,
}

public sealed record EventRecurrenceRule
{
    public required RecurrenceFrequency Frequency { get; init; }
    public int Interval { get; init; } = 1;
    public IReadOnlyList<IsoDayOfWeek> ByDay { get; init; } = [];
    public int? Count { get; init; }
    public LocalDate? UntilDate { get; init; }
}

public sealed record EventRecurrenceInfo
{
    public bool IsRecurring { get; init; }
    public bool CanEdit { get; init; } = true;
    public EventRecurrenceRule? Rule { get; init; }
    public string Summary { get; init; } = "Does not repeat";
}

public record CalendarEvent
{
    public required EventReference Reference { get; set; }

    public LocalDateTime StartTime { get; set; }
    public LocalDateTime EndTime { get; set; }
    public string? Title { get; set; }
    public DateTime? ChangedAt { get; set; }

    /// <summary>
    /// The user's response status to this event invitation.
    /// </summary>
    public EventResponseStatus ResponseStatus { get; set; } = EventResponseStatus.None;

    public ModelExtensions Extensions { get; init; } = new();
}

public record RawEvent
{
    public required EventReference Reference { get; init; }
    public required string RawData { get; init; }
}

public abstract record RichText
{
    public record SimpleText(string value) : RichText;
    public record HTML(string value) : RichText;
}

public record CalendarEventAttachment
{
    public required string Title { get; init; }
    public required string Url { get; init; }
}

public record CalendarEventParticipant
{
    public required string Email { get; init; }
    public string? Name { get; init; }
    public EventResponseStatus Status { get; init; } = EventResponseStatus.None;
    public bool IsOrganizer { get; init; }
    public bool IsOptional { get; init; }
}

public record CalendarEventConference
{
    public record EntryPoint
    {
        public required string Label { get; init; }
        public required string Uri { get; init; }
        public string? AdditionalInfo { get; set; }
    }

    public required string Name { get; init; }

    public required List<EntryPoint> EntryPoints { get; init; }
}

public record ParticipationActions
{
    public Func<Task>? Accept { get; init; }
    public Func<Task>? Decline { get; init; }
    public Func<Task>? Tentative { get; init; }
}

public record Participation
{
    public required EventResponseStatus CurrentState { get; init; }
    public ParticipationActions? Actions { get; init; }
}

public static class CalendarEventExtensions
{
    public static readonly ModelExtension<bool> FullDay = new();
    public static readonly ModelExtension<string> TimeZone = new();
    public static readonly ModelExtension<RichText> Description = new();
    public static readonly ModelExtension<string> Location = new();
    public static readonly ModelExtension<List<CalendarEventParticipant>> Participants = new();
    public static readonly ModelExtension<List<CalendarEventAttachment>> Attachments = new();
    public static readonly ModelExtension<CalendarEventConference> Conference = new();
    public static readonly ModelExtension<Participation> Participation = new();
    public static readonly ModelExtension<int> ReminderMinutesBefore = new();
    /// <summary>
    /// True when the event is transparent (free) and should not block the organizer's time.
    /// Absent (default false) means blocking — the safe fallback for providers that do not set it.
    /// </summary>
    public static readonly ModelExtension<bool> NonBlocking = new();
    public static readonly ModelExtension<RecurrenceEditInfo> RecurrenceEdit = new();
    public static readonly ModelExtension<EventRecurrenceInfo> RecurrenceInfo = new();
}

