using System.Collections.Generic;
using System.Linq;
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
public partial class ParticipantAvailabilityViewModel(
    string email,
    bool isOrganizerRow = false,
    string? displayName = null)
    : ObservableObject
{
    public string Email { get; } = email;

    /// <summary>True for the organizer's own row; renders with per-event calendar colours.</summary>
    public bool IsOrganizerRow { get; } = isOrganizerRow;

    [ObservableProperty]
    public partial string DisplayName { get; set; } = displayName ?? email;

    [ObservableProperty]
    public partial FreeBusyStatus Status { get; set; } = FreeBusyStatus.Unknown;

    [ObservableProperty]
    public partial bool IsLoading { get; set; } = true;

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
            var clampedEnd = slot.End > displayWindow.End ? displayWindow.End : slot.End;

            if (clampedStart >= clampedEnd) continue;

            var startFraction = (clampedStart - displayWindow.Start).TotalSeconds / windowDuration;
            var widthFraction = (clampedEnd - clampedStart).TotalSeconds / windowDuration;

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
        Status = FreeBusyStatus.Ok;
        IsLoading = false;
        OnPropertyChanged(nameof(IsUnknown));

        var windowDuration = (displayWindow.End - displayWindow.Start).TotalSeconds;
        if (windowDuration <= 0) return;

        // Clamp each event to the display window, drop ones entirely outside it,
        // then sort by clamped start time.
        var clamped = events
            .Select(ev => (
                Start: ev.Start < displayWindow.Start ? displayWindow.Start : ev.Start,
                End: ev.End > displayWindow.End ? displayWindow.End : ev.End,
                ev.Title))
            .Where(e => e.Start < e.End)
            .OrderBy(e => e.Start)
            .ToList();

        if (clamped.Count == 0) return;

        // Merge overlapping / touching intervals; accumulate all titles per merged slot.
        var mergeStart = clamped[0].Start;
        var mergeEnd = clamped[0].End;
        var titles = new List<string> { clamped[0].Title };

        for (var i = 1; i < clamped.Count; i++)
        {
            var ev = clamped[i];
            if (ev.Start <= mergeEnd)
            {
                if (ev.End > mergeEnd) mergeEnd = ev.End;
                titles.Add(ev.Title);
            }
            else
            {
                EmitSlot(mergeStart, mergeEnd, titles, displayWindow.Start, windowDuration);
                mergeStart = ev.Start;
                mergeEnd = ev.End;
                titles = [ev.Title];
            }
        }

        EmitSlot(mergeStart, mergeEnd, titles, displayWindow.Start, windowDuration);
    }

    private void EmitSlot(
        Instant start, Instant end, List<string> titles,
        Instant windowStart, double windowDuration)
    {
        var sf = (start - windowStart).TotalSeconds / windowDuration;
        var wf = (end - start).TotalSeconds / windowDuration;
        OwnEvents.Add(new OwnEventSlot(sf, wf, titles.AsReadOnly()));
    }
}

/// <summary>A busy interval expressed as fractional offsets [0,1] within the display window.</summary>
public record BusyRange(double Start, double Width);

/// <summary>
/// An own calendar event projected into fractional offsets [0,1] within the display window.
/// Overlapping events are merged; <see cref="OwnEventSlot.Titles"/> carries all event titles
/// within the merged span for tooltip display.
/// </summary>
public record OwnEventSlot(double Start, double Width, IReadOnlyList<string> Titles);