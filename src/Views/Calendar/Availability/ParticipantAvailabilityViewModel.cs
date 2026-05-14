using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NodaTime;
using perinma.Services;

namespace perinma.Views.Calendar.Availability;

/// <summary>
/// Represents one participant row in the availability timeline.
/// Busy slots are stored as fractional offsets [0,1] relative to the display window
/// so the renderer needs no time arithmetic — only pixel math.
/// </summary>
public partial class ParticipantAvailabilityViewModel : ObservableObject
{
    public string Email { get; }

    [ObservableProperty]
    private string _displayName;

    [ObservableProperty]
    private FreeBusyStatus _status = FreeBusyStatus.Unknown;

    [ObservableProperty]
    private bool _isLoading = true;

    /// <summary>
    /// Pre-computed busy ranges as (Start, Width) fractions in [0,1] within the display window.
    /// </summary>
    public ObservableCollection<BusyRange> BusyRanges { get; } = [];

    public bool IsUnknown => Status is FreeBusyStatus.Unknown or FreeBusyStatus.Unavailable;

    public ParticipantAvailabilityViewModel(string email, string? displayName = null)
    {
        Email = email;
        _displayName = displayName ?? email;
    }

    /// <summary>
    /// Applies freebusy data, projecting busy slots into fractional canvas coordinates
    /// relative to <paramref name="displayWindow"/>.
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
            // Clamp to display window
            var clampedStart = slot.Start < displayWindow.Start ? displayWindow.Start : slot.Start;
            var clampedEnd = slot.End > displayWindow.End ? displayWindow.End : slot.End;

            if (clampedStart >= clampedEnd)
                continue;

            var startFraction = (clampedStart - displayWindow.Start).TotalSeconds / windowDuration;
            var widthFraction = (clampedEnd - clampedStart).TotalSeconds / windowDuration;

            BusyRanges.Add(new BusyRange(startFraction, widthFraction));
        }

        OnPropertyChanged(nameof(IsUnknown));
    }
}

/// <summary>
/// A busy interval expressed as fractional offsets [0,1] within the display window.
/// </summary>
public record BusyRange(double Start, double Width);
