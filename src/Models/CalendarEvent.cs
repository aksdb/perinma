using System;
using System.Collections.Generic;
using Tmds.DBus.Protocol;

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

public class Extension<T> where T : ExtensionValue
{
    internal Extension() {}
}

public static class Extensions
{
    public static Extension<ExtensionValue.Text> Description = new();
    public static Extension<ExtensionValue.Text> Location = new();
    public static Extension<ExtensionValue.ValueList<ExtensionValue.Text>> Participants = new();
}

public class ExtensionValues
{
    private readonly Dict<Type, ExtensionValue> _valueByExtension = [];
    
    public ExtensionValue? this[Extension<ExtensionValue> extension]
    {
        get => _valueByExtension.TryGetValue(extension, out var value) ? value : null;
        set
        {
            if (value is null)
                _valueByExtension.Remove(extension);
            else
                _valueByExtension[extension] = value;
        }
    }

    public void Set<T>(Extension<T> extension, ExtensionValue value) where T: ExtensionValue => _valueByExtension[extension.GetType()] = value;
    
    public bool Get(Extension extension, out ExtensionValue value) => _valueByExtension.TryGetValue(extension, out value);
}

public abstract record ExtensionValue
{
    public record Text(string value) : ExtensionValue;
    public record ValueList<T>(List<T> list) : ExtensionValue where T: ExtensionValue;
}