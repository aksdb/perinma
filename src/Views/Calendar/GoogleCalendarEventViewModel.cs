using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Json;
using perinma.Models;
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
    
    public ObservableCollection<EventAttachment> Attachments { get; } = [];

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
                    
                    ExtractAttachments(googleEvent);
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
}
