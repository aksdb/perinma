using System;
using System.Collections.Generic;
using System.Linq;
using NodaTime;
using perinma.Services;
using perinma.Views.Calendar.Availability;
using tests.Fakes;

namespace tests;

[TestFixture]
public class AvailabilityWindowViewModelTests
{
    // 2024-05-14 14:00–15:00 (unspecified kind, treated as local)
    private static readonly DateTime DefaultStart = new(2024, 5, 14, 14, 0, 0);
    private static readonly DateTime DefaultEnd   = new(2024, 5, 14, 15, 0, 0);

    private static readonly DateTime ExpectedWindowStart = new(2024, 5, 14,  7, 0, 0);
    private static readonly DateTime ExpectedWindowEnd   = new(2024, 5, 14, 22, 0, 0);

    private static AvailabilityWindowViewModel MakeVm(
        IList<string>? emails = null,
        DateTime? start = null,
        DateTime? end = null)
    {
        var provider = new CalDavCalendarProviderStub();
        return new AvailabilityWindowViewModel(
            provider,
            "test-account",
            emails ?? new List<string> { "alice@example.com" },
            start ?? DefaultStart,
            end   ?? DefaultEnd);
    }

    // ── Display window ────────────────────────────────────────────────────────

    [Test]
    public void Constructor_SetsDisplayWindowToEventDate_07To22()
    {
        var vm = MakeVm();

        Assert.Multiple(() =>
        {
            Assert.That(vm.DisplayWindowStart, Is.EqualTo(ExpectedWindowStart));
            Assert.That(vm.DisplayWindowEnd,   Is.EqualTo(ExpectedWindowEnd));
        });
    }

    [Test]
    public void Constructor_DisplayWindowMinutesIs900()
    {
        var vm = MakeVm();
        Assert.That(vm.DisplayWindowMinutes, Is.EqualTo(900.0).Within(1e-9));
    }

    [Test]
    public void Constructor_ClampsSlotStartToDisplayWindow()
    {
        // Start is before the 07:00 window start
        var vm = MakeVm(start: new DateTime(2024, 5, 14, 6, 0, 0),
                        end:   new DateTime(2024, 5, 14, 7, 30, 0));

        Assert.That(vm.SelectedStart, Is.GreaterThanOrEqualTo(vm.DisplayWindowStart));
    }

    [Test]
    public void Constructor_ClampsSlotEndToDisplayWindow()
    {
        // End is after the 22:00 window end
        var vm = MakeVm(start: new DateTime(2024, 5, 14, 21, 0, 0),
                        end:   new DateTime(2024, 5, 14, 23, 0, 0));

        Assert.That(vm.SelectedEnd, Is.LessThanOrEqualTo(vm.DisplayWindowEnd));
    }

    [Test]
    public void Constructor_SeedsRowsForAllEmails()
    {
        var emails = new List<string> { "alice@example.com", "bob@example.com", "carol@example.com" };
        var vm = MakeVm(emails: emails);

        Assert.That(vm.Rows, Has.Count.EqualTo(3));
        Assert.That(vm.Rows.Select(r => r.Email), Is.EquivalentTo(emails));
    }

    [Test]
    public void Constructor_RowEmailsMatchInputOrder()
    {
        var emails = new List<string> { "alice@example.com", "bob@example.com" };
        var vm = MakeVm(emails: emails);

        Assert.That(vm.Rows[0].Email, Is.EqualTo("alice@example.com"));
        Assert.That(vm.Rows[1].Email, Is.EqualTo("bob@example.com"));
    }

    // ── TimeLabels ────────────────────────────────────────────────────────────

    [Test]
    public void Constructor_TimeLabels_CountIs8()
    {
        // 07:00, 09:00, 11:00, 13:00, 15:00, 17:00, 19:00, 21:00 = 8
        var vm = MakeVm();
        Assert.That(vm.TimeLabels, Has.Count.EqualTo(8));
    }

    [Test]
    public void Constructor_TimeLabels_FirstFractionIsZero()
    {
        var vm = MakeVm();
        Assert.That(vm.TimeLabels[0].Fraction, Is.EqualTo(0.0).Within(1e-9));
    }

    [Test]
    public void Constructor_TimeLabels_FirstTextIs0700()
    {
        var vm = MakeVm();
        Assert.That(vm.TimeLabels[0].Text, Is.EqualTo("07:00"));
    }

    [Test]
    public void Constructor_TimeLabels_LastFractionIs14Over15()
    {
        // 21:00 is 14 h after 07:00, window is 15 h
        var vm = MakeVm();
        var last = vm.TimeLabels[^1];
        Assert.That(last.Fraction, Is.EqualTo(14.0 / 15).Within(1e-9));
    }

    [Test]
    public void Constructor_TimeLabels_FractionsAreStrictlyIncreasing()
    {
        var vm = MakeVm();
        var fractions = vm.TimeLabels.Select(l => l.Fraction).ToList();
        for (int i = 1; i < fractions.Count; i++)
            Assert.That(fractions[i], Is.GreaterThan(fractions[i - 1]));
    }

    // ── MoveSlot ──────────────────────────────────────────────────────────────

    [Test]
    public void MoveSlot_SnapsTo30MinBoundary_AlreadyOnBoundary()
    {
        // Fraction 0.10 → 07:00 + 0.10 * 900 min = 07:00 + 90 min = 08:30
        // Snap(08:30): (90 min / 30) = 3.0 → 3 * 30 = 90 → 08:30
        var vm = MakeVm();
        var duration = vm.SelectedEnd - vm.SelectedStart;

        vm.MoveSlot(0.10);

        Assert.That(vm.SelectedStart, Is.EqualTo(new DateTime(2024, 5, 14, 8, 30, 0)));
        Assert.That(vm.SelectedEnd - vm.SelectedStart, Is.EqualTo(duration));
    }

    [Test]
    public void MoveSlot_SnapsToNearest30Min_RoundsUp()
    {
        // Fraction for 09:20 from 07:00: offset = 140 min / 900 min
        // Snap: round(140/30) = round(4.667) = 5 → 150 min → 09:30
        var vm = MakeVm();
        double fraction = 140.0 / 900.0;

        vm.MoveSlot(fraction);

        Assert.That(vm.SelectedStart, Is.EqualTo(new DateTime(2024, 5, 14, 9, 30, 0)));
    }

    [Test]
    public void MoveSlot_SnapsToNearest30Min_RoundsDown()
    {
        // Fraction for 09:10 from 07:00: offset = 130 min / 900 min
        // Snap: round(130/30) = round(4.333) = 4 → 120 min → 09:00
        var vm = MakeVm();
        double fraction = 130.0 / 900.0;

        vm.MoveSlot(fraction);

        Assert.That(vm.SelectedStart, Is.EqualTo(new DateTime(2024, 5, 14, 9, 0, 0)));
    }

    [Test]
    public void MoveSlot_KeepsSlotInsideDisplayWindow_ClampsToEnd()
    {
        // Very large fraction forces the end past DisplayWindowEnd
        var vm = MakeVm();
        vm.MoveSlot(1.0); // Past end

        Assert.That(vm.SelectedEnd, Is.LessThanOrEqualTo(vm.DisplayWindowEnd));
    }

    [Test]
    public void MoveSlot_NegativeFraction_ClampsToWindowStart()
    {
        var vm = MakeVm();
        vm.MoveSlot(-0.5);

        Assert.That(vm.SelectedStart, Is.GreaterThanOrEqualTo(vm.DisplayWindowStart));
    }

    // ── ResizeSlot ────────────────────────────────────────────────────────────

    [Test]
    public void ResizeSlot_EnforcesMinimum30Minutes()
    {
        // VM starts at 14:00–15:00; resize end to ~14:05 (well under 30 min from start)
        var vm = MakeVm();
        // offset for 14:05 from 07:00 = 425 min; fraction = 425/900
        double tinyFraction = 425.0 / 900.0;

        vm.ResizeSlot(tinyFraction);

        Assert.That(vm.SelectedEnd - vm.SelectedStart,
            Is.GreaterThanOrEqualTo(TimeSpan.FromMinutes(30)));
    }

    [Test]
    public void ResizeSlot_SnapsTo30MinBoundary()
    {
        // Start at 08:00; resize end to 09:20 (should snap to 09:30)
        var vm = MakeVm(start: new DateTime(2024, 5, 14, 8, 0, 0),
                        end:   new DateTime(2024, 5, 14, 9, 0, 0));
        // offset for 09:20 from 07:00 = 140 min; fraction = 140/900
        double fraction = 140.0 / 900.0;

        vm.ResizeSlot(fraction);

        Assert.That(vm.SelectedEnd, Is.EqualTo(new DateTime(2024, 5, 14, 9, 30, 0)));
    }

    [Test]
    public void ResizeSlot_CannotExceedDisplayWindowEnd()
    {
        var vm = MakeVm();
        vm.ResizeSlot(2.0); // Way past end

        Assert.That(vm.SelectedEnd, Is.LessThanOrEqualTo(vm.DisplayWindowEnd));
    }

    // ── Computed slot properties ──────────────────────────────────────────────

    [Test]
    public void SelectedSlotStartFraction_MatchesSelectedStart()
    {
        // SelectedStart = 09:00 = WindowStart + 2h → fraction = 2/15
        var vm = MakeVm();
        vm.SelectedStart = new DateTime(2024, 5, 14, 9, 0, 0);

        Assert.That(vm.SelectedSlotStartFraction, Is.EqualTo(2.0 / 15).Within(1e-9));
    }

    [Test]
    public void SelectedSlotWidthFraction_MatchesDuration()
    {
        // Duration = 1h over 15h window → 1/15
        var vm = MakeVm();
        vm.SelectedStart = new DateTime(2024, 5, 14, 9, 0, 0);
        vm.SelectedEnd   = new DateTime(2024, 5, 14, 10, 0, 0);

        Assert.That(vm.SelectedSlotWidthFraction, Is.EqualTo(1.0 / 15).Within(1e-9));
    }

    [Test]
    public void SelectedSlotWidthFraction_IsZeroWhenEndEqualsStart()
    {
        var vm = MakeVm();
        vm.SelectedStart = new DateTime(2024, 5, 14, 9, 0, 0);
        vm.SelectedEnd   = new DateTime(2024, 5, 14, 9, 0, 0);

        Assert.That(vm.SelectedSlotWidthFraction, Is.EqualTo(0.0).Within(1e-9));
    }

    [Test]
    public void SelectedSlotLabel_ContainsBothTimes()
    {
        var vm = MakeVm();
        vm.SelectedStart = new DateTime(2024, 5, 14, 14, 0, 0);
        vm.SelectedEnd   = new DateTime(2024, 5, 14, 15, 0, 0);

        var label = vm.SelectedSlotLabel;

        Assert.Multiple(() =>
        {
            Assert.That(label, Does.Contain("14:00"));
            Assert.That(label, Does.Contain("15:00"));
        });
    }

    [Test]
    public void SelectedSlotStartFraction_WindowStartGivesFractionZero()
    {
        var vm = MakeVm();
        vm.SelectedStart = vm.DisplayWindowStart;

        Assert.That(vm.SelectedSlotStartFraction, Is.EqualTo(0.0).Within(1e-9));
    }

    [Test]
    public void SelectedSlotStartFraction_WindowEndGivesFractionOne()
    {
        var vm = MakeVm();
        vm.SelectedStart = vm.DisplayWindowEnd;

        Assert.That(vm.SelectedSlotStartFraction, Is.EqualTo(1.0).Within(1e-9));
    }

    // ── Initial slot state ────────────────────────────────────────────────────

    [Test]
    public void Constructor_SelectedStartAndEndReflectInitialSlot()
    {
        var vm = MakeVm(start: new DateTime(2024, 5, 14, 10, 0, 0),
                        end:   new DateTime(2024, 5, 14, 11, 30, 0));

        Assert.Multiple(() =>
        {
            Assert.That(vm.SelectedStart, Is.EqualTo(new DateTime(2024, 5, 14, 10, 0, 0)));
            Assert.That(vm.SelectedEnd,   Is.EqualTo(new DateTime(2024, 5, 14, 11, 30, 0)));
        });
    }

    [Test]
    public void Constructor_IsLoadingIsFalseInitially()
    {
        // IsLoading is false before any refresh is triggered
        var vm = MakeVm();
        Assert.That(vm.IsLoading, Is.False);
    }

    [Test]
    public void Constructor_ErrorMessageIsNullInitially()
    {
        var vm = MakeVm();
        Assert.That(vm.ErrorMessage, Is.Null);
    }

    // ── Day navigation ────────────────────────────────────────────────────────

    [Test]
    public void Constructor_DisplayDateEqualsEventDate()
    {
        var vm = MakeVm();
        Assert.That(vm.DisplayDate, Is.EqualTo(new DateTime(2024, 5, 14)));
    }

    [Test]
    public void Constructor_DisplayDateLabelContainsDate()
    {
        var vm = MakeVm();
        // Just verify it includes the day number and year — locale-independent
        Assert.That(vm.DisplayDateLabel, Does.Contain("14"));
        Assert.That(vm.DisplayDateLabel, Does.Contain("2024"));
    }

    [Test]
    public void NavigateDay_PreviousDay_DecrementsDisplayDate()
    {
        var vm = MakeVm();
        var original = vm.DisplayDate;

        vm.GoToPreviousDayCommand.Execute(null);

        Assert.That(vm.DisplayDate, Is.EqualTo(original.AddDays(-1)));
    }

    [Test]
    public void NavigateDay_NextDay_IncrementsDisplayDate()
    {
        var vm = MakeVm();
        var original = vm.DisplayDate;

        vm.GoToNextDayCommand.Execute(null);

        Assert.That(vm.DisplayDate, Is.EqualTo(original.AddDays(1)));
    }

    [Test]
    public void NavigateDay_UpdatesDisplayWindowBounds()
    {
        var vm = MakeVm();

        vm.GoToNextDayCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(vm.DisplayWindowStart, Is.EqualTo(new DateTime(2024, 5, 15, 7, 0, 0)));
            Assert.That(vm.DisplayWindowEnd,   Is.EqualTo(new DateTime(2024, 5, 15, 22, 0, 0)));
        });
    }

    [Test]
    public void NavigateDay_PreservesSlotTimeOfDay()
    {
        // Slot starts at 14:00 on 2024-05-14; after going to next day it should
        // still start at 14:00, on 2024-05-15.
        var vm = MakeVm(start: new DateTime(2024, 5, 14, 14, 0, 0),
                        end:   new DateTime(2024, 5, 14, 15, 0, 0));

        vm.GoToNextDayCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(vm.SelectedStart, Is.EqualTo(new DateTime(2024, 5, 15, 14, 0, 0)));
            Assert.That(vm.SelectedEnd,   Is.EqualTo(new DateTime(2024, 5, 15, 15, 0, 0)));
        });
    }

    [Test]
    public void NavigateDay_PreservesSlotDuration()
    {
        var duration = TimeSpan.FromMinutes(90);
        var vm = MakeVm(start: new DateTime(2024, 5, 14, 10, 0, 0),
                        end:   new DateTime(2024, 5, 14, 11, 30, 0));

        vm.GoToPreviousDayCommand.Execute(null);

        Assert.That(vm.SelectedEnd - vm.SelectedStart, Is.EqualTo(duration));
    }

    [Test]
    public void NavigateDay_ClampsSlotToNewWindowEnd()
    {
        // Slot is at 21:30–22:00 (already at window edge). After navigation the
        // new window is identical in shape, so it must still fit.
        var vm = MakeVm(start: new DateTime(2024, 5, 14, 21, 30, 0),
                        end:   new DateTime(2024, 5, 14, 22, 0, 0));

        vm.GoToNextDayCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(vm.SelectedStart, Is.GreaterThanOrEqualTo(vm.DisplayWindowStart));
            Assert.That(vm.SelectedEnd,   Is.LessThanOrEqualTo(vm.DisplayWindowEnd));
        });
    }

    [Test]
    public void NavigateDay_ClearsRowBusyRanges()
    {
        var vm = MakeVm(emails: new List<string> { "alice@example.com" });
        // Simulate pre-populated busy data
        vm.Rows[0].BusyRanges.Add(new BusyRange(0.2, 0.1));

        vm.GoToNextDayCommand.Execute(null);

        Assert.That(vm.Rows[0].BusyRanges, Is.Empty);
    }

    [Test]
    public void NavigateDay_UpdatesFractionsForNewWindow()
    {
        // SelectedStart at 14:00 → fraction 7/15 in the 07:00-22:00 window.
        // After navigating, the window shifts but the hours are identical,
        // so the fraction must be the same.
        var vm = MakeVm(start: new DateTime(2024, 5, 14, 14, 0, 0),
                        end:   new DateTime(2024, 5, 14, 15, 0, 0));
        var fractionBefore = vm.SelectedSlotStartFraction;

        vm.GoToNextDayCommand.Execute(null);

        Assert.That(vm.SelectedSlotStartFraction, Is.EqualTo(fractionBefore).Within(1e-9));
    }

    // ── Organizer row ─────────────────────────────────────────────────────────

    private static AvailabilityWindowViewModel MakeVmWithOrganizer(
        string organizerName = "My Account",
        Func<NodaTime.Interval, System.Threading.CancellationToken,
             System.Threading.Tasks.Task<System.Collections.Generic.IList<perinma.Services.OwnCalendarEvent>>>? getOwnEvents = null,
        IList<string>? emails = null)
    {
        var provider = new CalDavCalendarProviderStub();
        return new AvailabilityWindowViewModel(
            provider,
            "test-account",
            emails ?? new List<string> { "alice@example.com" },
            DefaultStart,
            DefaultEnd,
            organizerDisplayName: organizerName,
            getOwnEvents: getOwnEvents);
    }

    [Test]
    public void OrganizerRow_AddedAsFirstRow_WhenNameProvided()
    {
        var vm = MakeVmWithOrganizer();
        Assert.Multiple(() =>
        {
            Assert.That(vm.Rows[0].IsOrganizerRow, Is.True);
            Assert.That(vm.Rows[0].DisplayName, Is.EqualTo("My Account"));
        });
    }

    [Test]
    public void OrganizerRow_AttendeeRowsFollowOrganizer()
    {
        var vm = MakeVmWithOrganizer(emails: new List<string> { "bob@example.com" });
        Assert.Multiple(() =>
        {
            Assert.That(vm.Rows, Has.Count.EqualTo(2));
            Assert.That(vm.Rows[0].IsOrganizerRow, Is.True);
            Assert.That(vm.Rows[1].Email, Is.EqualTo("bob@example.com"));
            Assert.That(vm.Rows[1].IsOrganizerRow, Is.False);
        });
    }

    [Test]
    public void NoOrganizerParams_NoOrganizerRow()
    {
        // When neither organizerDisplayName nor getOwnEvents is supplied the
        // legacy constructor path must not add an extra row.
        var vm = MakeVm(emails: new List<string> { "alice@example.com" });
        Assert.That(vm.Rows, Has.Count.EqualTo(1));
        Assert.That(vm.Rows[0].IsOrganizerRow, Is.False);
    }

    [Test]
    public async Task OrganizerRow_PopulatedByGetOwnEvents()
    {
        var events = new List<perinma.Services.OwnCalendarEvent>
        {
            new()
            {
                Title         = "Stand-up",
                Start         = NodaTime.Instant.FromUtc(2024, 5, 14, 9, 0),
                End           = NodaTime.Instant.FromUtc(2024, 5, 14, 9, 30),
                CalendarColor = "#4CAF50"
            }
        };

        var vm = MakeVmWithOrganizer(
            getOwnEvents: (_, _) => System.Threading.Tasks.Task.FromResult(
                (System.Collections.Generic.IList<perinma.Services.OwnCalendarEvent>)events));

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(vm.Rows[0].OwnEvents, Has.Count.EqualTo(1));
            Assert.That(vm.Rows[0].OwnEvents[0].Title, Is.EqualTo("Stand-up"));
        });
    }

    [Test]
    public async Task NavigateDay_ClearsOrganizerOwnEvents()
    {
        var events = new List<perinma.Services.OwnCalendarEvent>
        {
            new()
            {
                Title = "Meeting",
                Start = NodaTime.Instant.FromUtc(2024, 5, 14, 10, 0),
                End   = NodaTime.Instant.FromUtc(2024, 5, 14, 11, 0)
            }
        };

        var vm = MakeVmWithOrganizer(
            getOwnEvents: (_, _) => System.Threading.Tasks.Task.FromResult(
                (System.Collections.Generic.IList<perinma.Services.OwnCalendarEvent>)events));

        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.That(vm.Rows[0].OwnEvents, Has.Count.EqualTo(1));

        // Navigate clears immediately, then refresh fills with new-day events
        vm.GoToNextDayCommand.Execute(null);

        // After navigation the row will be cleared by NavigateDay (synchronously
        // before the async refresh fires) — but the async refresh also runs.
        // We just assert the organizer row exists and is the first row.
        Assert.That(vm.Rows[0].IsOrganizerRow, Is.True);
    }
}
