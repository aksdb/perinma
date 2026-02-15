using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using perinma.Models;

namespace perinma.Views.Calendar.EventView;

public partial class ParticipantsViewModel(List<CalendarEventParticipant> participants) : ViewModelBase
{
    public ObservableCollection<ParticipantItemViewModel> Participants { get; } =
        [..participants.Select(p => new ParticipantItemViewModel(p))];
}

public partial class ParticipantItemViewModel(CalendarEventParticipant participant) : ViewModelBase
{
    [ObservableProperty]
    private string _name = participant.Name ?? participant.Email;

    [ObservableProperty]
    private string _email = participant.Email;

    [ObservableProperty]
    private EventResponseStatus _status = participant.Status;

    [ObservableProperty]
    private bool _isOrganizer = participant.IsOrganizer;

    public string Initials => GetInitials(Name, Email);

    public bool HasStatus => Status != EventResponseStatus.None;

    public string StatusIcon => Status switch
    {
        EventResponseStatus.Accepted => "\u2713",    // ✓ checkmark
        EventResponseStatus.Declined => "\u2717",    // ✗ cross
        EventResponseStatus.Tentative => "?",        // question mark
        EventResponseStatus.NeedsAction => "\u2022", // • bullet
        _ => ""
    };

    public string StatusColor => Status switch
    {
        EventResponseStatus.Accepted => "#10B981",   // emerald
        EventResponseStatus.Declined => "#EF4444",   // red
        EventResponseStatus.Tentative => "#F59E0B",  // amber
        EventResponseStatus.NeedsAction => "#6B7280", // gray
        _ => "#6B7280"
    };

    public string StatusTooltip => Status switch
    {
        EventResponseStatus.Accepted => "Accepted",
        EventResponseStatus.Declined => "Declined",
        EventResponseStatus.Tentative => "Tentative",
        EventResponseStatus.NeedsAction => "Not responded",
        _ => "Unknown"
    };

    private static string GetInitials(string name, string email)
    {
        var nameToUse = string.IsNullOrWhiteSpace(name) ? email : name;

        if (string.IsNullOrWhiteSpace(nameToUse))
            return "?";

        // If it looks like an email, extract initials from local part
        if (nameToUse.Contains('@'))
        {
            var localPart = nameToUse.Split('@')[0];
            // Try to split by dots or underscores
            var parts = localPart.Split(['.', '_', '-'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                return $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant();
            }
            if (parts.Length == 1 && parts[0].Length >= 2)
            {
                return parts[0][..2].ToUpperInvariant();
            }
            return parts[0][0].ToString().ToUpperInvariant();
        }

        // Regular name - split by spaces
        var nameParts = nameToUse.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (nameParts.Length >= 2)
        {
            return $"{nameParts[0][0]}{nameParts[^1][0]}".ToUpperInvariant();
        }
        if (nameParts.Length == 1 && nameParts[0].Length >= 1)
        {
            return nameParts[0][0].ToString().ToUpperInvariant();
        }

        return "?";
    }
}
