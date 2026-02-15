using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NodaTime;
using perinma.Models;

namespace perinma.Views.Calendar.EventView;

public partial class CalendarEventViewModel : ViewModelBase
{
    [ObservableProperty]
    private CalendarEvent _calendarEvent;

    public ObservableCollection<ViewModelBase> EventDetails { get; } = [];

    public CalendarEventViewModel(CalendarEvent calendarEvent)
    {
        CalendarEvent = calendarEvent;
    }
    
    public CalendarEventViewModel() : this(new CalendarEvent
    {
        Reference = new EventReference
        {
            Calendar = new Models.Calendar
            {
                Account = new Account { Id = Guid.NewGuid(), Name = "Design Account" },
                Id = Guid.NewGuid()
            },
            Id = Guid.NewGuid()
        },
        StartTime = LocalDateTime.FromDateTime(DateTime.UtcNow),
        EndTime = LocalDateTime.FromDateTime(DateTime.UtcNow.AddHours(1))
    })
    {
        if (Avalonia.Controls.Design.IsDesignMode)
        {
            PopulateEventDetails();
        }
    }

    partial void OnCalendarEventChanged(CalendarEvent? oldValue, CalendarEvent newValue)
    {
        PopulateEventDetails();
    }

    private void PopulateEventDetails()
    {
        EventDetails.Clear();

        var fullDayValue = CalendarEvent.Extensions.Get(CalendarEventExtensions.FullDay);
        var fullDay = fullDayValue;
        EventDetails.Add(new TimeRangeViewModel(
            CalendarEvent.StartTime,
            CalendarEvent.EndTime,
            fullDay
        ));

        var location = CalendarEvent.Extensions.Get(CalendarEventExtensions.Location);
        if (!string.IsNullOrEmpty(location))
            EventDetails.Add(new SimpleTextViewModel
            {
                Label = "Location",
                Content = location
            });

        var conference = CalendarEvent.Extensions.Get(CalendarEventExtensions.Conference);
        if (conference != null)
            EventDetails.Add(new ConferenceViewModel(conference));

        var description = CalendarEvent.Extensions.Get(CalendarEventExtensions.Description);
        if (description != null)
            EventDetails.Add(new RichTextViewModel("Description", description));

        var participants = CalendarEvent.Extensions.Get(CalendarEventExtensions.Participants);
        if (participants is { Count: > 0 })
        {
            var participantsVm = new ParticipantsViewModel(participants);
            EventDetails.Add(participantsVm);
            _ = participantsVm.EnrichParticipantsAsync();
        }

        var attachments = CalendarEvent.Extensions.Get(CalendarEventExtensions.Attachments);
        if (attachments is { Count: > 0 })
            EventDetails.Add(new AttachmentsViewModel(attachments));

        var participation = CalendarEvent.Extensions.Get(CalendarEventExtensions.Participation);
        if (participation != null)
            EventDetails.Add(new ParticipationViewModel(participation));
    }
}