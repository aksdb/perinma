using System;
using System.Collections.Generic;

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

    public ZonedDateTime StartTime { get; set; }
    public ZonedDateTime EndTime { get; set; }
    public string? Title { get; set; }
    public DateTime? ChangedAt { get; set; }

    /// <summary>
    /// The user's response status to this event invitation.
    /// </summary>
    public EventResponseStatus ResponseStatus { get; set; } = EventResponseStatus.None;

    public ExtensionValues Extensions { get; init; } = new();
}

public record RawEvent
{
    public required EventReference Reference { get; init; }
    public required string RawData { get; init; }
}

public class Extension<T> where T : class
{
    internal Extension()
    {
    }
}

public static class Extensions
{
    public static Extension<string> Description = new();
    public static Extension<string> Location = new();
    public static Extension<List<string>> Participants = new();
}

public class ExtensionValues
{
    private readonly Dictionary<object, object> _valueByExtension = [];

    public void Set<T>(Extension<T> extension, T value) where T : class =>
        _valueByExtension[extension] = value;

    public T? Get<T>(Extension<T> extension) where T : class =>
        _valueByExtension.GetValueOrDefault(extension) as T;
}