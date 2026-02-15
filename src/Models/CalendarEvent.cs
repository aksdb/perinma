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
    public static ModelExtension<bool> FullDay = new();
    public static ModelExtension<string> TimeZone = new();
    public static ModelExtension<RichText> Description = new();
    public static ModelExtension<string> Location = new();
    public static ModelExtension<List<CalendarEventParticipant>> Participants = new();
    public static ModelExtension<List<CalendarEventAttachment>> Attachments = new();
    public static ModelExtension<CalendarEventConference> Conference = new();
    public static ModelExtension<Participation> Participation = new();
}
