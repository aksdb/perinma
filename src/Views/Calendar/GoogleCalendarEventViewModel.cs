using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Json;
using perinma.Models;
using perinma.Services;
using perinma.Services.Google;
using perinma.Storage;
using perinma.Storage.Models;

namespace perinma.Views.Calendar;

/// <summary>
/// Represents an attachment on a Google Calendar event.
/// </summary>
public class EventAttachment
{
    public required string Title { get; init; }
    public required string FileUrl { get; init; }
    public string? IconLink { get; init; }
    public string? MimeType { get; init; }
}

public partial class GoogleCalendarEventViewModel : ViewModelBase, IRespondableEventViewModel
{
    public CalendarEvent CalendarEvent => _calendarEvent;

    private readonly CalendarEvent _calendarEvent;
    private readonly SqliteStorage _storage;
    private readonly ICalendarProvider? _calendarProvider;

    [ObservableProperty]
    private string _googleMeetLink = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _location = string.Empty;

    [ObservableProperty]
    private string _organizer = string.Empty;

    [ObservableProperty]
    private string _creator = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isUpdating;

    [ObservableProperty]
    private bool _canRespond;

    [ObservableProperty]
    private EventResponseStatus _currentResponseStatus = EventResponseStatus.None;

    public ObservableCollection<EventAttachment> Attachments { get; } = [];

    public ObservableCollection<Models.EventAttendee> Attendees { get; } = [];

    public GoogleCalendarEventViewModel(
        CalendarEvent calendarEvent,
        SqliteStorage storage,
        ICalendarProvider? calendarProvider = null)
    {
        _calendarEvent = calendarEvent;
        _storage = storage;
        _calendarProvider = calendarProvider;
        _isLoading = true;
        _ = LoadEventDataAsync();
    }

    private async Task LoadEventDataAsync()
    {
        try
        {
            var rawData = await _storage.GetEventData(_calendarEvent.Id.ToString(), "rawData");
            if (!string.IsNullOrEmpty(rawData))
            {
                var googleEvent = NewtonsoftJsonSerializer.Instance.Deserialize<Event>(rawData);
                if (googleEvent != null)
                {
                    GoogleMeetLink = ExtractGoogleMeetLink(googleEvent);
                    Description = googleEvent.Description ?? string.Empty;
                    Location = googleEvent.Location ?? string.Empty;

                    Organizer = googleEvent.Organizer != null
                        ? $"{googleEvent.Organizer.DisplayName ?? googleEvent.Organizer.Email}"
                        : string.Empty;

                    Creator = googleEvent.Creator != null
                        ? $"{googleEvent.Creator.DisplayName ?? googleEvent.Creator.Email}"
                        : string.Empty;

                    ExtractAttendees(googleEvent);
                    ExtractAttachments(googleEvent);

                    // Check if user can respond (is an attendee and not the organizer)
                    var selfAttendee = googleEvent.Attendees?.FirstOrDefault(a => a.Self == true);
                    CanRespond = selfAttendee != null && !(selfAttendee.Organizer ?? false);
                    CurrentResponseStatus = _calendarEvent.ResponseStatus;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load Google Calendar event data: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ExtractAttendees(Event googleEvent)
    {
        if (googleEvent.Attendees == null)
        {
            return;
        }

        foreach (var attendee in googleEvent.Attendees)
        {
            var name = attendee.DisplayName ?? attendee.Email;
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var responseStatus = ParseGoogleResponseStatus(attendee.ResponseStatus);

            Attendees.Add(new Models.EventAttendee
            {
                Name = name,
                ResponseStatus = responseStatus,
                IsOrganizer = attendee.Organizer ?? false
            });
        }
    }

    private static EventResponseStatus ParseGoogleResponseStatus(string? status)
    {
        if (string.IsNullOrEmpty(status))
        {
            return EventResponseStatus.None;
        }

        return status.ToLowerInvariant() switch
        {
            "accepted" => EventResponseStatus.Accepted,
            "declined" => EventResponseStatus.Declined,
            "tentative" => EventResponseStatus.Tentative,
            "needsaction" => EventResponseStatus.NeedsAction,
            _ => EventResponseStatus.None
        };
    }

    private void ExtractAttachments(Event googleEvent)
    {
        if (googleEvent.Attachments == null)
        {
            return;
        }

        foreach (var attachment in googleEvent.Attachments)
        {
            if (string.IsNullOrEmpty(attachment.FileUrl))
            {
                continue;
            }

            Attachments.Add(new EventAttachment
            {
                Title = attachment.Title ?? attachment.FileUrl,
                FileUrl = attachment.FileUrl,
                IconLink = attachment.IconLink,
                MimeType = attachment.MimeType
            });
        }
    }
    
    private string ExtractGoogleMeetLink(Event googleEvent)
    {
        if (!string.IsNullOrEmpty(googleEvent.HangoutLink))
        {
            return googleEvent.HangoutLink;
        }

        if (googleEvent.ConferenceData != null &&
            googleEvent.ConferenceData.EntryPoints != null)
        {
            foreach (var entryPoint in googleEvent.ConferenceData.EntryPoints)
            {
                if (entryPoint.EntryPointType == "video" && !string.IsNullOrEmpty(entryPoint.Uri))
                {
                    return entryPoint.Uri;
                }
            }
        }

        if (!string.IsNullOrEmpty(googleEvent.Description))
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                googleEvent.Description,
                @"https?://meet\.google\.com/[a-z0-9\-]+",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (match.Success)
            {
                return match.Value;
            }
        }

        return string.Empty;
    }

    [RelayCommand]
    private async Task AcceptEventAsync()
    {
        await RespondToEventAsync("accepted");
    }

    [RelayCommand]
    private async Task DeclineEventAsync()
    {
        await RespondToEventAsync("declined");
    }

    [RelayCommand]
    private async Task TentativeEventAsync()
    {
        await RespondToEventAsync("tentative");
    }

    private async Task RespondToEventAsync(string responseStatus)
    {
        if (IsUpdating || !CanRespond)
        {
            return;
        }

        if (_calendarProvider == null)
        {
            Console.WriteLine("Calendar provider not available");
            return;
        }

        try
        {
            IsUpdating = true;

            var accountId = _calendarEvent.Calendar.Account.Id.ToString();
            var calendarId = _calendarEvent.Calendar.ExternalId;
            var eventId = _calendarEvent.ExternalId;

            if (string.IsNullOrEmpty(calendarId) || string.IsNullOrEmpty(eventId))
            {
                Console.WriteLine("Missing calendar or event ID");
                return;
            }

            // Get raw event data for the provider
            var rawData = await _storage.GetEventData(_calendarEvent.Id.ToString(), "rawData");
            if (string.IsNullOrEmpty(rawData))
            {
                Console.WriteLine("Failed to get raw event data");
                return;
            }

            // Use the provider to respond to the event
            await _calendarProvider.RespondToEventAsync(accountId, calendarId, eventId, rawData, responseStatus);

            // Update local state
            CurrentResponseStatus = responseStatus switch
            {
                "accepted" => EventResponseStatus.Accepted,
                "declined" => EventResponseStatus.Declined,
                "tentative" => EventResponseStatus.Tentative,
                _ => EventResponseStatus.None
            };

            // Update the attendee list to reflect the change
            var selfAttendee = Attendees.FirstOrDefault(a => a.Name == _calendarEvent.Calendar.Account.Name);
            if (selfAttendee != null)
            {
                var index = Attendees.IndexOf(selfAttendee);
                Attendees[index] = new Models.EventAttendee
                {
                    Name = selfAttendee.Name,
                    ResponseStatus = CurrentResponseStatus,
                    IsOrganizer = selfAttendee.IsOrganizer
                };
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to respond to event: {ex.Message}");
        }
        finally
        {
            IsUpdating = false;
        }
    }
}
