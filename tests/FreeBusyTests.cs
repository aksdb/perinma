using System;
using System.Collections.Generic;
using NodaTime;
using perinma.Services;
using perinma.Views.Calendar.Availability;

namespace tests;

[TestFixture]
public class FreeBusyTests
{
    // ── TimeSlot record ───────────────────────────────────────────────────────

    [Test]
    public void TimeSlot_RecordEquality_EqualWhenSameInstants()
    {
        var start = Instant.FromUtc(2024, 5, 14, 9, 0);
        var end = Instant.FromUtc(2024, 5, 14, 10, 0);

        var a = new TimeSlot(start, end);
        var b = new TimeSlot(start, end);

        Assert.That(a, Is.EqualTo(b));
    }

    [Test]
    public void TimeSlot_RecordEquality_NotEqualWhenDifferent()
    {
        var a = new TimeSlot(Instant.FromUtc(2024, 5, 14, 9, 0), Instant.FromUtc(2024, 5, 14, 10, 0));
        var b = new TimeSlot(Instant.FromUtc(2024, 5, 14, 9, 0), Instant.FromUtc(2024, 5, 14, 11, 0));

        Assert.That(a, Is.Not.EqualTo(b));
    }

    [Test]
    public void TimeSlot_Properties_RetainValues()
    {
        var start = Instant.FromUtc(2024, 5, 14, 9, 0);
        var end = Instant.FromUtc(2024, 5, 14, 10, 0);
        var slot = new TimeSlot(start, end);

        Assert.Multiple(() =>
        {
            Assert.That(slot.Start, Is.EqualTo(start));
            Assert.That(slot.End, Is.EqualTo(end));
        });
    }

    // ── AttendeeFreeBusy record ───────────────────────────────────────────────

    [Test]
    public void AttendeeFreeBusy_OkStatus_HasNonNullBusySlots()
    {
        var fb = new AttendeeFreeBusy
        {
            Email = "alice@example.com",
            Status = FreeBusyStatus.Ok,
            BusySlots = new List<TimeSlot>
            {
                new(Instant.FromUtc(2024, 5, 14, 9, 0), Instant.FromUtc(2024, 5, 14, 10, 0))
            }
        };

        Assert.That(fb.BusySlots, Is.Not.Null);
        Assert.That(fb.BusySlots, Has.Count.EqualTo(1));
    }

    [Test]
    public void AttendeeFreeBusy_UnknownStatus_DefaultsToEmptyBusySlots()
    {
        var fb = new AttendeeFreeBusy
        {
            Email = "alice@example.com",
            Status = FreeBusyStatus.Unknown
        };

        Assert.That(fb.BusySlots, Is.Not.Null);
        Assert.That(fb.BusySlots, Is.Empty);
    }

    [Test]
    public void AttendeeFreeBusy_DefaultStatus_IsOk()
    {
        var fb = new AttendeeFreeBusy { Email = "alice@example.com" };
        Assert.That(fb.Status, Is.EqualTo(FreeBusyStatus.Ok));
    }

    // ── ParticipantAvailabilityViewModel.Apply ────────────────────────────────
    // Display window: 2024-05-14 07:00 UTC – 22:00 UTC (54000 s)

    private static readonly Instant WindowStart = Instant.FromUtc(2024, 5, 14, 7, 0);
    private static readonly Instant WindowEnd = Instant.FromUtc(2024, 5, 14, 22, 0);
    private static readonly Interval DisplayWindow = new(WindowStart, WindowEnd);
    private const double WindowSeconds = 15.0 * 3600; // 54000

    [Test]
    public void Apply_NoBusySlots_BusyRangesIsEmpty()
    {
        var vm = new ParticipantAvailabilityViewModel("alice@example.com");
        var fb = new AttendeeFreeBusy { Email = "alice@example.com", Status = FreeBusyStatus.Ok };

        vm.Apply(fb, DisplayWindow);

        Assert.Multiple(() =>
        {
            Assert.That(vm.BusyRanges, Is.Empty);
            Assert.That(vm.Status, Is.EqualTo(FreeBusyStatus.Ok));
        });
    }

    [Test]
    public void Apply_OneBusySlotFullyInsideWindow_CorrectFractions()
    {
        // 09:00–10:00 UTC: offset from 07:00 = 2h = 7200 s; width = 1h = 3600 s
        // startFraction = 7200 / 54000 = 2/15
        // widthFraction = 3600 / 54000 = 1/15
        var vm = new ParticipantAvailabilityViewModel("alice@example.com");
        var fb = new AttendeeFreeBusy
        {
            Email = "alice@example.com",
            Status = FreeBusyStatus.Ok,
            BusySlots = new List<TimeSlot>
            {
                new(Instant.FromUtc(2024, 5, 14, 9, 0), Instant.FromUtc(2024, 5, 14, 10, 0))
            }
        };

        vm.Apply(fb, DisplayWindow);

        Assert.That(vm.BusyRanges, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(vm.BusyRanges[0].Start, Is.EqualTo(2.0 / 15).Within(1e-9));
            Assert.That(vm.BusyRanges[0].Width, Is.EqualTo(1.0 / 15).Within(1e-9));
        });
    }

    [Test]
    public void Apply_SlotStartsBeforeWindowStart_ClampedToWindowStart()
    {
        // Slot 06:00–08:00 UTC; clamped start = 07:00, clamped end = 08:00
        // startFraction = 0, widthFraction = 3600/54000 = 1/15
        var vm = new ParticipantAvailabilityViewModel("alice@example.com");
        var fb = new AttendeeFreeBusy
        {
            Email = "alice@example.com",
            Status = FreeBusyStatus.Ok,
            BusySlots = new List<TimeSlot>
            {
                new(Instant.FromUtc(2024, 5, 14, 6, 0), Instant.FromUtc(2024, 5, 14, 8, 0))
            }
        };

        vm.Apply(fb, DisplayWindow);

        Assert.That(vm.BusyRanges, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(vm.BusyRanges[0].Start, Is.EqualTo(0.0).Within(1e-9));
            Assert.That(vm.BusyRanges[0].Width, Is.EqualTo(1.0 / 15).Within(1e-9));
        });
    }

    [Test]
    public void Apply_SlotEndsAfterWindowEnd_ClampedToWindowEnd()
    {
        // Slot 21:00–23:00 UTC; clamped start = 21:00, clamped end = 22:00
        // startFraction = (21:00 - 07:00) / 15h = 14/15
        // widthFraction = (22:00 - 21:00) / 15h = 1/15
        var vm = new ParticipantAvailabilityViewModel("alice@example.com");
        var fb = new AttendeeFreeBusy
        {
            Email = "alice@example.com",
            Status = FreeBusyStatus.Ok,
            BusySlots = new List<TimeSlot>
            {
                new(Instant.FromUtc(2024, 5, 14, 21, 0), Instant.FromUtc(2024, 5, 14, 23, 0))
            }
        };

        vm.Apply(fb, DisplayWindow);

        Assert.That(vm.BusyRanges, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(vm.BusyRanges[0].Start, Is.EqualTo(14.0 / 15).Within(1e-9));
            Assert.That(vm.BusyRanges[0].Width, Is.EqualTo(1.0 / 15).Within(1e-9));
        });
    }

    [Test]
    public void Apply_SlotEntirelyBeforeWindow_BusyRangesEmpty()
    {
        var vm = new ParticipantAvailabilityViewModel("alice@example.com");
        var fb = new AttendeeFreeBusy
        {
            Email = "alice@example.com",
            Status = FreeBusyStatus.Ok,
            BusySlots = new List<TimeSlot>
            {
                new(Instant.FromUtc(2024, 5, 14, 4, 0), Instant.FromUtc(2024, 5, 14, 6, 0))
            }
        };

        vm.Apply(fb, DisplayWindow);

        Assert.That(vm.BusyRanges, Is.Empty);
    }

    [Test]
    public void Apply_SlotEntirelyAfterWindow_BusyRangesEmpty()
    {
        var vm = new ParticipantAvailabilityViewModel("alice@example.com");
        var fb = new AttendeeFreeBusy
        {
            Email = "alice@example.com",
            Status = FreeBusyStatus.Ok,
            BusySlots = new List<TimeSlot>
            {
                new(Instant.FromUtc(2024, 5, 14, 23, 0), Instant.FromUtc(2024, 5, 15, 0, 0))
            }
        };

        vm.Apply(fb, DisplayWindow);

        Assert.That(vm.BusyRanges, Is.Empty);
    }

    [Test]
    public void Apply_UnknownStatus_IsUnknownTrue_BusyRangesEmpty()
    {
        var vm = new ParticipantAvailabilityViewModel("alice@example.com");
        var fb = new AttendeeFreeBusy
        {
            Email = "alice@example.com",
            Status = FreeBusyStatus.Unknown
        };

        vm.Apply(fb, DisplayWindow);

        Assert.Multiple(() =>
        {
            Assert.That(vm.IsUnknown, Is.True);
            Assert.That(vm.BusyRanges, Is.Empty);
        });
    }

    [Test]
    public void Apply_UnavailableStatus_IsUnknownTrue_BusyRangesEmpty()
    {
        var vm = new ParticipantAvailabilityViewModel("alice@example.com");
        var fb = new AttendeeFreeBusy
        {
            Email = "alice@example.com",
            Status = FreeBusyStatus.Unavailable
        };

        vm.Apply(fb, DisplayWindow);

        Assert.Multiple(() =>
        {
            Assert.That(vm.IsUnknown, Is.True);
            Assert.That(vm.BusyRanges, Is.Empty);
        });
    }

    [Test]
    public void Apply_SetsDisplayNameFromFreeBusy()
    {
        var vm = new ParticipantAvailabilityViewModel("alice@example.com");
        var fb = new AttendeeFreeBusy
        {
            Email = "alice@example.com",
            DisplayName = "Alice Smith",
            Status = FreeBusyStatus.Ok
        };

        vm.Apply(fb, DisplayWindow);

        Assert.That(vm.DisplayName, Is.EqualTo("Alice Smith"));
    }

    [Test]
    public void Apply_ClearsExistingBusyRangesBeforeApplying()
    {
        var vm = new ParticipantAvailabilityViewModel("alice@example.com");

        // Apply once with a slot
        vm.Apply(new AttendeeFreeBusy
        {
            Email = "alice@example.com",
            Status = FreeBusyStatus.Ok,
            BusySlots = new List<TimeSlot>
            {
                new(Instant.FromUtc(2024, 5, 14, 9, 0), Instant.FromUtc(2024, 5, 14, 10, 0))
            }
        }, DisplayWindow);

        Assert.That(vm.BusyRanges, Has.Count.EqualTo(1));

        // Apply again with no slots
        vm.Apply(new AttendeeFreeBusy
        {
            Email = "alice@example.com",
            Status = FreeBusyStatus.Ok
        }, DisplayWindow);

        Assert.That(vm.BusyRanges, Is.Empty);
    }

    // ── ParticipantAvailabilityViewModel.ApplyOwnEvents ───────────────────────

    private static OwnCalendarEvent MakeEvent(
        int startH, int endH, string title = "Meeting", string? color = null) =>
        new()
        {
            Title         = title,
            Start         = Instant.FromUtc(2024, 5, 14, startH, 0),
            End           = Instant.FromUtc(2024, 5, 14, endH, 0),
            CalendarColor = color
        };

    [Test]
    public void ApplyOwnEvents_EmptyList_LeavesOwnEventsEmpty()
    {
        var vm = new ParticipantAvailabilityViewModel("me", isOrganizerRow: true);
        vm.ApplyOwnEvents(new List<OwnCalendarEvent>(), DisplayWindow);
        Assert.That(vm.OwnEvents, Is.Empty);
    }

    [Test]
    public void ApplyOwnEvents_SetsStatusOkAndIsLoadingFalse()
    {
        var vm = new ParticipantAvailabilityViewModel("me", isOrganizerRow: true);
        vm.ApplyOwnEvents(new List<OwnCalendarEvent> { MakeEvent(9, 10) }, DisplayWindow);
        Assert.Multiple(() =>
        {
            Assert.That(vm.Status,    Is.EqualTo(FreeBusyStatus.Ok));
            Assert.That(vm.IsLoading, Is.False);
        });
    }

    [Test]
    public void ApplyOwnEvents_EventWithinWindow_ProjectedCorrectly()
    {
        // 09:00–10:00 in a 07:00–22:00 window (15 h = 54000 s)
        // start fraction = 2/15, width fraction = 1/15
        var vm = new ParticipantAvailabilityViewModel("me", isOrganizerRow: true);
        vm.ApplyOwnEvents(new List<OwnCalendarEvent> { MakeEvent(9, 10) }, DisplayWindow);

        Assert.That(vm.OwnEvents, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(vm.OwnEvents[0].Start,      Is.EqualTo(2.0 / 15).Within(1e-9));
            Assert.That(vm.OwnEvents[0].Width,      Is.EqualTo(1.0 / 15).Within(1e-9));
            Assert.That(vm.OwnEvents[0].Titles[0],  Is.EqualTo("Meeting"));
        });
    }

    [Test]
    public void ApplyOwnEvents_PreservesTitle()
    {
        var vm = new ParticipantAvailabilityViewModel("me", isOrganizerRow: true);
        vm.ApplyOwnEvents(new List<OwnCalendarEvent>
        {
            MakeEvent(9, 10, title: "Stand-up")
        }, DisplayWindow);

        Assert.That(vm.OwnEvents[0].Titles, Is.EqualTo(new[] { "Stand-up" }));
    }

    [Test]
    public void ApplyOwnEvents_EventBeforeWindow_IsDropped()
    {
        // Event ends before window start
        var vm = new ParticipantAvailabilityViewModel("me", isOrganizerRow: true);
        var ev = new OwnCalendarEvent
        {
            Title = "Early",
            Start = Instant.FromUtc(2024, 5, 14, 5, 0),
            End   = Instant.FromUtc(2024, 5, 14, 6, 0)
        };
        vm.ApplyOwnEvents(new List<OwnCalendarEvent> { ev }, DisplayWindow);
        Assert.That(vm.OwnEvents, Is.Empty);
    }

    [Test]
    public void ApplyOwnEvents_EventAfterWindow_IsDropped()
    {
        var vm = new ParticipantAvailabilityViewModel("me", isOrganizerRow: true);
        var ev = new OwnCalendarEvent
        {
            Title = "Late",
            Start = Instant.FromUtc(2024, 5, 14, 22, 30),
            End   = Instant.FromUtc(2024, 5, 14, 23, 0)
        };
        vm.ApplyOwnEvents(new List<OwnCalendarEvent> { ev }, DisplayWindow);
        Assert.That(vm.OwnEvents, Is.Empty);
    }

    [Test]
    public void ApplyOwnEvents_EventStraddlingWindowStart_ClampedToStart()
    {
        // Event starts before 07:00, ends at 08:00
        var vm = new ParticipantAvailabilityViewModel("me", isOrganizerRow: true);
        var ev = new OwnCalendarEvent
        {
            Title = "Straddle",
            Start = Instant.FromUtc(2024, 5, 14, 6, 0),
            End   = Instant.FromUtc(2024, 5, 14, 8, 0)
        };
        vm.ApplyOwnEvents(new List<OwnCalendarEvent> { ev }, DisplayWindow);

        Assert.That(vm.OwnEvents, Has.Count.EqualTo(1));
        // Clamped start → fraction 0; clamped width = 1 h / 15 h
        Assert.Multiple(() =>
        {
            Assert.That(vm.OwnEvents[0].Start, Is.EqualTo(0.0).Within(1e-9));
            Assert.That(vm.OwnEvents[0].Width, Is.EqualTo(1.0 / 15).Within(1e-9));
        });
    }

    [Test]
    public void ApplyOwnEvents_CalledTwice_ReplacesOwnEvents()
    {
        var vm = new ParticipantAvailabilityViewModel("me", isOrganizerRow: true);
        vm.ApplyOwnEvents(new List<OwnCalendarEvent> { MakeEvent(9, 10) }, DisplayWindow);
        Assert.That(vm.OwnEvents, Has.Count.EqualTo(1));

        vm.ApplyOwnEvents(new List<OwnCalendarEvent>(), DisplayWindow);
        Assert.That(vm.OwnEvents, Is.Empty);
    }

    // ── Merging ───────────────────────────────────────────────────────────────

    [Test]
    public void ApplyOwnEvents_TwoNonOverlapping_TwoSlots()
    {
        // 09:00–10:00 and 11:00–12:00 — gap between them, so no merging
        var vm = new ParticipantAvailabilityViewModel("me", isOrganizerRow: true);
        vm.ApplyOwnEvents(new List<OwnCalendarEvent>
        {
            MakeEvent(9, 10, "A"),
            MakeEvent(11, 12, "B")
        }, DisplayWindow);

        Assert.That(vm.OwnEvents, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(vm.OwnEvents[0].Titles, Is.EqualTo(new[] { "A" }));
            Assert.That(vm.OwnEvents[1].Titles, Is.EqualTo(new[] { "B" }));
        });
    }

    [Test]
    public void ApplyOwnEvents_TwoOverlapping_MergedIntoOne()
    {
        // 09:00–10:30 overlaps 10:00–11:00 → merged 09:00–11:00
        var vm = new ParticipantAvailabilityViewModel("me", isOrganizerRow: true);
        vm.ApplyOwnEvents(new List<OwnCalendarEvent>
        {
            MakeEvent(9, 10, "Stand-up"),
            new() { Title = "Review",
                    Start = Instant.FromUtc(2024, 5, 14, 9, 30),
                    End   = Instant.FromUtc(2024, 5, 14, 10, 30) }
        }, DisplayWindow);

        Assert.That(vm.OwnEvents, Has.Count.EqualTo(1));
        // Merged span: 09:00–10:30 → start 2/15, width 1.5/15
        Assert.Multiple(() =>
        {
            Assert.That(vm.OwnEvents[0].Start, Is.EqualTo(2.0 / 15).Within(1e-9));
            Assert.That(vm.OwnEvents[0].Width, Is.EqualTo(1.5 / 15).Within(1e-9));
            Assert.That(vm.OwnEvents[0].Titles, Is.EquivalentTo(new[] { "Stand-up", "Review" }));
        });
    }

    [Test]
    public void ApplyOwnEvents_TouchingEvents_MergedIntoOne()
    {
        // 09:00–10:00 touches 10:00–11:00 (end == next start) → merged
        var vm = new ParticipantAvailabilityViewModel("me", isOrganizerRow: true);
        vm.ApplyOwnEvents(new List<OwnCalendarEvent>
        {
            MakeEvent(9, 10, "First"),
            MakeEvent(10, 11, "Second")
        }, DisplayWindow);

        Assert.That(vm.OwnEvents, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(vm.OwnEvents[0].Width, Is.EqualTo(2.0 / 15).Within(1e-9));
            Assert.That(vm.OwnEvents[0].Titles, Is.EquivalentTo(new[] { "First", "Second" }));
        });
    }

    [Test]
    public void ApplyOwnEvents_OutOfOrderInput_StillMergesCorrectly()
    {
        // Supply in reverse order — sort must happen before merging
        var vm = new ParticipantAvailabilityViewModel("me", isOrganizerRow: true);
        vm.ApplyOwnEvents(new List<OwnCalendarEvent>
        {
            MakeEvent(11, 12, "B"),
            MakeEvent(9,  10, "A")
        }, DisplayWindow);

        Assert.That(vm.OwnEvents, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(vm.OwnEvents[0].Titles[0], Is.EqualTo("A"));
            Assert.That(vm.OwnEvents[1].Titles[0], Is.EqualTo("B"));
        });
    }

    [Test]
    public void ApplyOwnEvents_ContainedEvent_ExpandsNotCreatesNewSlot()
    {
        // 09:00–12:00 fully contains 10:00–11:00 → one slot, two titles, end stays 12:00
        var vm = new ParticipantAvailabilityViewModel("me", isOrganizerRow: true);
        vm.ApplyOwnEvents(new List<OwnCalendarEvent>
        {
            MakeEvent(9, 12, "All-Day"),
            MakeEvent(10, 11, "Inner")
        }, DisplayWindow);

        Assert.That(vm.OwnEvents, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            // start 2/15, width 3/15
            Assert.That(vm.OwnEvents[0].Start, Is.EqualTo(2.0 / 15).Within(1e-9));
            Assert.That(vm.OwnEvents[0].Width, Is.EqualTo(3.0 / 15).Within(1e-9));
            Assert.That(vm.OwnEvents[0].Titles, Is.EquivalentTo(new[] { "All-Day", "Inner" }));
        });
    }
}
