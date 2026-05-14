using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NodaTime;
using NodaTime.Extensions;
using perinma.Services;

namespace perinma.Views.Calendar.Availability;

/// <summary>
/// ViewModel for the availability dialog window.
/// Holds the participant rows, the display window (07:00–22:00 on the event's date),
/// and the draggable proposed-slot overlay (SelectedStart / SelectedEnd).
///
/// The dialog is constructed with fixed inputs; data is fetched once at open time
/// and can be refreshed explicitly via <see cref="RefreshCommand"/>.
/// </summary>
public partial class AvailabilityWindowViewModel : ObservableObject
{
    // ── Display window ──────────────────────────────────────────────────────

    /// <summary>Local 07:00 on the event's date.</summary>
    public DateTime DisplayWindowStart { get; }
    /// <summary>Local 22:00 on the event's date.</summary>
    public DateTime DisplayWindowEnd { get; }

    /// <summary>Span of the display window in minutes (900 = 15 h).</summary>
    public double DisplayWindowMinutes { get; }

    /// <summary>Hour labels rendered along the top of the timeline (every 2 h).</summary>
    public IReadOnlyList<TimeLabel> TimeLabels { get; }

    // ── Participant rows ─────────────────────────────────────────────────────

    public ObservableCollection<ParticipantAvailabilityViewModel> Rows { get; } = [];

    // ── State ────────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    // ── Selected slot (draggable overlay) ────────────────────────────────────

    /// <summary>Start of the proposed time slot (local).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedSlotLabel))]
    [NotifyPropertyChangedFor(nameof(SelectedSlotStartFraction))]
    [NotifyPropertyChangedFor(nameof(SelectedSlotWidthFraction))]
    private DateTime _selectedStart;

    /// <summary>End of the proposed time slot (local).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedSlotLabel))]
    [NotifyPropertyChangedFor(nameof(SelectedSlotStartFraction))]
    [NotifyPropertyChangedFor(nameof(SelectedSlotWidthFraction))]
    private DateTime _selectedEnd;

    /// <summary>Fraction [0,1] of the slot start within the display window.</summary>
    public double SelectedSlotStartFraction =>
        ToFraction(SelectedStart);

    /// <summary>Fraction [0,1] of the slot width within the display window.</summary>
    public double SelectedSlotWidthFraction =>
        Math.Max(0, ToFraction(SelectedEnd) - ToFraction(SelectedStart));

    /// <summary>Human-readable label shown in the confirm button area.</summary>
    public string SelectedSlotLabel =>
        $"{SelectedStart:ddd d MMM, HH:mm} – {SelectedEnd:HH:mm}";

    // ── Private state ─────────────────────────────────────────────────────────

    private readonly ICalendarProvider _provider;
    private readonly string _accountId;
    private readonly IList<string> _attendeeEmails;
    private CancellationTokenSource? _cts;

    // ─────────────────────────────────────────────────────────────────────────

    public AvailabilityWindowViewModel(
        ICalendarProvider provider,
        string accountId,
        IList<string> attendeeEmails,
        DateTime initialStart,
        DateTime initialEnd)
    {
        _provider = provider;
        _accountId = accountId;
        _attendeeEmails = attendeeEmails;

        // Display window: event date 07:00–22:00 local
        var eventDate = initialStart.Date;
        DisplayWindowStart = eventDate.AddHours(7);
        DisplayWindowEnd = eventDate.AddHours(22);
        DisplayWindowMinutes = (DisplayWindowEnd - DisplayWindowStart).TotalMinutes;

        // Clamp the initial slot to the display window
        _selectedStart = Clamp(initialStart, DisplayWindowStart, DisplayWindowEnd.AddMinutes(-30));
        _selectedEnd = Clamp(initialEnd, _selectedStart.AddMinutes(30), DisplayWindowEnd);

        // Build time labels every 2 hours
        TimeLabels = BuildTimeLabels();

        // Seed empty rows so the window is populated immediately
        foreach (var email in attendeeEmails)
            Rows.Add(new ParticipantAvailabilityViewModel(email));
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync(CancellationToken ct)
    {
        _cts?.Cancel();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var linkedCt = _cts.Token;

        IsLoading = true;
        ErrorMessage = null;

        // Mark every row as loading
        foreach (var row in Rows)
        {
            row.IsLoading = true;
            row.BusyRanges.Clear();
        }

        try
        {
            var windowStart = DisplayWindowStart.ToInstant();
            var windowEnd = DisplayWindowEnd.ToInstant();
            var displayInterval = new Interval(windowStart, windowEnd);
            // Query a slightly wider range than the display window so edge-spanning events are included
            var queryInterval = new Interval(
                windowStart.Minus(Duration.FromHours(1)),
                windowEnd.Plus(Duration.FromHours(1)));

            var results = await _provider.GetFreeBusyAsync(
                _accountId, _attendeeEmails, queryInterval, linkedCt);

            // Merge into existing rows (preserving display-name from contacts)
            var lookup = results.ToDictionary(
                r => r.Email, r => r, StringComparer.OrdinalIgnoreCase);

            foreach (var row in Rows)
            {
                if (lookup.TryGetValue(row.Email, out var fb))
                    row.Apply(fb, displayInterval);
                else
                    row.Apply(new AttendeeFreeBusy
                    {
                        Email = row.Email,
                        Status = FreeBusyStatus.Unknown
                    }, displayInterval);
            }
        }
        catch (OperationCanceledException)
        {
            // Refresh was superseded or window closed — no-op
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load availability: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanRefresh() => !IsLoading;

    // ── Slot movement (called from code-behind on pointer drag) ───────────────

    private static readonly TimeSpan SnapInterval = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Moves the slot start to the given fraction within the display window,
    /// preserving the current duration and snapping to 30-minute boundaries.
    /// </summary>
    public void MoveSlot(double startFraction)
    {
        var duration = SelectedEnd - SelectedStart;
        var rawStart = FractionToDateTime(startFraction);
        var snapped = Snap(rawStart);
        var snappedEnd = snapped + duration;

        // Keep slot inside display window
        if (snappedEnd > DisplayWindowEnd)
        {
            snappedEnd = DisplayWindowEnd;
            snapped = snappedEnd - duration;
        }
        if (snapped < DisplayWindowStart)
            snapped = DisplayWindowStart;

        SelectedStart = snapped;
        SelectedEnd = snappedEnd;
    }

    /// <summary>
    /// Resizes the slot by moving its end to the given fraction,
    /// snapping to 30-minute boundaries and enforcing a minimum of 30 minutes.
    /// </summary>
    public void ResizeSlot(double endFraction)
    {
        var rawEnd = FractionToDateTime(endFraction);
        var snapped = Snap(rawEnd);
        var minimum = SelectedStart + SnapInterval;
        if (snapped < minimum) snapped = minimum;
        if (snapped > DisplayWindowEnd) snapped = DisplayWindowEnd;
        SelectedEnd = snapped;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private double ToFraction(DateTime local)
    {
        var totalSeconds = (DisplayWindowEnd - DisplayWindowStart).TotalSeconds;
        if (totalSeconds <= 0) return 0;
        var offsetSeconds = (local - DisplayWindowStart).TotalSeconds;
        return Math.Clamp(offsetSeconds / totalSeconds, 0, 1);
    }

    private DateTime FractionToDateTime(double fraction)
    {
        var offsetSeconds = fraction * (DisplayWindowEnd - DisplayWindowStart).TotalSeconds;
        return DisplayWindowStart.AddSeconds(offsetSeconds);
    }

    private DateTime Snap(DateTime dt)
    {
        var totalMinutes = (dt - DisplayWindowStart).TotalMinutes;
        var snappedMinutes = Math.Round(totalMinutes / SnapInterval.TotalMinutes)
                             * SnapInterval.TotalMinutes;
        return DisplayWindowStart.AddMinutes(snappedMinutes);
    }

    private static DateTime Clamp(DateTime value, DateTime min, DateTime max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    private IReadOnlyList<TimeLabel> BuildTimeLabels()
    {
        var labels = new List<TimeLabel>();
        var cursor = DisplayWindowStart;
        var step = TimeSpan.FromHours(2);
        while (cursor <= DisplayWindowEnd)
        {
            labels.Add(new TimeLabel(ToFraction(cursor), cursor.ToString("HH:mm")));
            cursor += step;
        }
        return labels;
    }
}

/// <summary>Hour label for the timeline header: a fraction and a display string.</summary>
public record TimeLabel(double Fraction, string Text);
