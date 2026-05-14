using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NodaTime;
using perinma.Services;

namespace perinma.Views.Calendar.Availability;

/// <summary>
/// Represents one participant row in the availability timeline.
/// Busy slots are stored as fractional offsets [0,1] relative to the display window
/// so the renderer needs no time arithmetic — only pixel math.
///
/// For the organizer row (<see cref="IsOrganizerRow"/> == true) the data comes from
/// the local SQLite cache via <see cref="ApplyOwnEvents"/>; the richer
/// <see cref="OwnEvents"/> collection is populated instead of <see cref="BusyRanges"/>.
/// </summary>
public partial class ParticipantAvailabilityViewModel : ObservableObject
{
    public string Email { get; }

    /// <summary>True for the organizer's own row; renders with per-event calendar colours.</summary>
    public bool IsOrganizerRow { get; }

    [ObservableProperty]
    private string _displayName;

    [ObservableProperty]
    private FreeBusyStatus _status = FreeBusyStatus.Unknown;

    [ObservableProperty]
    private bool _isLoading = true;

    /// <summary>
    /// Pre-computed busy ranges as (Start, Width) fractions in [0,1] within the display window.
    /// Populated for attendee rows via <see cref="Apply"/>.
    /// </summary>
    public ObservableCollection<BusyRange> BusyRanges { get; } = [];

    /// <summary>
    /// Own calendar events as fractional (Start, Width) positions plus display metadata.
    /// Populated for the organizer row via <see cref="ApplyOwnEvents"/>.
    /// </summary>
    public ObservableCollection<OwnEventSlot> OwnEvents { get; } = [];

    public bool IsUnknown => Status is FreeBusyStatus.Unknown or FreeBusyStatus.Unavailable;

    public ParticipantAvailabilityViewModel(string email, bool isOrganizerRow = false, string? displayName = null)
    {
        Email = email;
        IsOrganizerRow = isOrganizerRow;
        _displayName = displayName ?? email;
    }

    /// <summary>
    /// Applies freebusy data for an attendee row, projecting busy slots into fractional
    /// canvas coordinates relative to <paramref name="displayWindow"/>.
    /// </summary>
    public void Apply(AttendeeFreeBusy freeBusy, Interval displayWindow)
    {
        DisplayName = freeBusy.DisplayName ?? freeBusy.Email;
        Status = freeBusy.Status;
        IsLoading = false;

        BusyRanges.Clear();

        if (freeBusy.Status != FreeBusyStatus.Ok)
        {
            OnPropertyChanged(nameof(IsUnknown));
            return;
        }

        var windowDuration = (displayWindow.End - displayWindow.Start).TotalSeconds;
        if (windowDuration <= 0)
            return;

        foreach (var slot in freeBusy.BusySlots)
        {
            var clampedStart = slot.Start < displayWindow.Start ? displayWindow.Start : slot.Start;
            var clampedEnd   = slot.End   > displayWindow.End   ? displayWindow.End   : slot.End;

            if (clampedStart >= clampedEnd) continue;

            var startFraction = (clampedStart - displayWindow.Start).TotalSeconds / windowDuration;
            var widthFraction = (clampedEnd   - clampedStart).TotalSeconds / windowDuration;

            BusyRanges.Add(new BusyRange(startFraction, widthFraction));
        }

        OnPropertyChanged(nameof(IsUnknown));
    }

    /// <summary>
    /// Applies own calendar events for the organizer row, projecting each event into
    /// fractional canvas coordinates relative to <paramref name="displayWindow"/>.
    /// Events wholly outside the window are silently dropped.
    /// </summary>
    public void ApplyOwnEvents(IList<OwnCalendarEvent> events, Interval displayWindow)
    {
        OwnEvents.Clear();
        Status    = FreeBusyStatus.Ok;
        IsLoading = false;
        OnPropertyChanged(nameof(IsUnknown));

        var windowDuration = (displayWindow.End - displayWindow.Start).TotalSeconds;
        if (windowDuration <= 0) return;

        foreach (var ev in events)
        {
            var clampedStart = ev.Start < displayWindow.Start ? displayWindow.Start : ev.Start;
            var clampedEnd   = ev.End   > displayWindow.End   ? displayWindow.End   : ev.End;

            if (clampedStart >= clampedEnd) continue;

            var startFraction = (clampedStart - displayWindow.Start).TotalSeconds / windowDuration;
            var widthFraction = (clampedEnd   - clampedStart).TotalSeconds / windowDuration;

            OwnEvents.Add(new OwnEventSlot(startFraction, widthFraction, ev.Title, ev.CalendarColor));
        }
    }
}

/// <summary>A busy interval expressed as fractional offsets [0,1] within the display window.</summary>
public record BusyRange(double Start, double Width);

/// <summary>
/// An own calendar event projected into fractional offsets [0,1] within the display window,
/// carrying display metadata (title, calendar colour) for the renderer.
/// </summary>
public record OwnEventSlot(double Start, double Width, string Title, string? CalendarColor);
