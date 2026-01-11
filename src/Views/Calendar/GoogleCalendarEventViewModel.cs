using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Json;
using perinma.Models;
using perinma.Storage;
using perinma.Storage.Models;

namespace perinma.Views.Calendar;

public partial class GoogleCalendarEventViewModel : ViewModelBase
{
    public CalendarEvent CalendarEvent => _calendarEvent;

    private readonly CalendarEvent _calendarEvent;
    private readonly SqliteStorage _storage;

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

    public GoogleCalendarEventViewModel(CalendarEvent calendarEvent, SqliteStorage storage)
    {
        _calendarEvent = calendarEvent;
        _storage = storage;
        _isLoading = true;
        _ = LoadEventDataAsync();
    }

    private async Task LoadEventDataAsync()
    {
        try
        {
            var eventDbo = new CalendarEventDbo
            {
                CalendarId = _calendarEvent.Calendar.Id.ToString(),
                EventId = _calendarEvent.Id.ToString(),
                ExternalId = _calendarEvent.ExternalId
            };

            var rawData = await _storage.GetEventData(eventDbo, "rawData");
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
}
