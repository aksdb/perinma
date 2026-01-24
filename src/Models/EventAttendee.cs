namespace perinma.Models;

/// <summary>
/// Represents an attendee of a calendar event with their response status.
/// </summary>
public class EventAttendee
{
    /// <summary>
    /// The display name or email of the attendee.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The attendee's response status to the event invitation.
    /// </summary>
    public EventResponseStatus ResponseStatus { get; init; } = EventResponseStatus.None;

    /// <summary>
    /// Whether this attendee is the organizer of the event.
    /// </summary>
    public bool IsOrganizer { get; init; }

    /// <summary>
    /// Gets the icon character representing the response status.
    /// </summary>
    public string StatusIcon => ResponseStatus switch
    {
        EventResponseStatus.Accepted => "\u2713",    // ✓ checkmark
        EventResponseStatus.Declined => "\u2717",    // ✗ cross
        EventResponseStatus.Tentative => "?",        // question mark
        EventResponseStatus.NeedsAction => "\u2022", // • bullet
        _ => ""
    };

    /// <summary>
    /// Gets the tooltip text for the response status.
    /// </summary>
    public string StatusTooltip => ResponseStatus switch
    {
        EventResponseStatus.Accepted => "Accepted",
        EventResponseStatus.Declined => "Declined",
        EventResponseStatus.Tentative => "Tentative",
        EventResponseStatus.NeedsAction => "Not responded",
        _ => "Unknown"
    };

    /// <summary>
    /// Gets the color for the status icon.
    /// </summary>
    public string StatusColor => ResponseStatus switch
    {
        EventResponseStatus.Accepted => "#22863a",   // green
        EventResponseStatus.Declined => "#cb2431",   // red
        EventResponseStatus.Tentative => "#b08800",  // amber
        EventResponseStatus.NeedsAction => "#6a737d", // gray
        _ => "#6a737d"
    };
}
