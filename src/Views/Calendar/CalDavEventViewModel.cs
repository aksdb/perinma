using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using perinma.Models;
using perinma.Storage;
using perinma.Storage.Models;
using ICalCalendar = Ical.Net.Calendar;

namespace perinma.Views.Calendar;

public partial class CalDavEventViewModel : ViewModelBase
{
    public CalendarEvent CalendarEvent => _calendarEvent;

    private readonly CalendarEvent _calendarEvent;
    private readonly SqliteStorage _storage;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _location = string.Empty;

    [ObservableProperty]
    private string _organizer = string.Empty;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private string _categories = string.Empty;

    [ObservableProperty]
    private string _url = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public ObservableCollection<EventAttendee> Attendees { get; } = [];

    public CalDavEventViewModel(CalendarEvent calendarEvent, SqliteStorage storage)
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
                var calendar = ICalCalendar.Load(rawData);
                var iCalEvent = calendar?.Events.FirstOrDefault();
                if (iCalEvent != null)
                {
                    Description = iCalEvent.Description ?? string.Empty;
                    Location = iCalEvent.Location ?? string.Empty;

                    // Extract organizer name or email
                    if (iCalEvent.Organizer != null)
                    {
                        Organizer = !string.IsNullOrEmpty(iCalEvent.Organizer.CommonName)
                            ? iCalEvent.Organizer.CommonName
                            : ExtractEmailFromUri(iCalEvent.Organizer.Value?.ToString());
                    }

                    // Format status (CONFIRMED, CANCELLED, TENTATIVE)
                    Status = FormatStatus(iCalEvent.Status);

                    // Extract categories/tags
                    if (iCalEvent.Categories != null && iCalEvent.Categories.Count > 0)
                    {
                        Categories = string.Join(", ", iCalEvent.Categories);
                    }

                    // Extract URL
                    if (iCalEvent.Url != null)
                    {
                        Url = iCalEvent.Url.ToString();
                    }

                    // Extract attendees with response status
                    if (iCalEvent.Attendees != null && iCalEvent.Attendees.Count > 0)
                    {
                        foreach (var attendee in iCalEvent.Attendees)
                        {
                            var name = !string.IsNullOrEmpty(attendee.CommonName)
                                ? attendee.CommonName
                                : ExtractEmailFromUri(attendee.Value?.ToString());

                            if (string.IsNullOrEmpty(name))
                            {
                                continue;
                            }

                            var responseStatus = ParseParticipationStatus(attendee.ParticipationStatus);

                            Attendees.Add(new EventAttendee
                            {
                                Name = name,
                                ResponseStatus = responseStatus
                            });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load CalDAV event data: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static string ExtractEmailFromUri(string? mailtoUri)
    {
        if (string.IsNullOrEmpty(mailtoUri))
        {
            return string.Empty;
        }

        // Remove "mailto:" prefix if present
        if (mailtoUri.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            return mailtoUri[7..];
        }

        return mailtoUri;
    }

    private static string FormatStatus(string? status)
    {
        if (string.IsNullOrEmpty(status))
        {
            return string.Empty;
        }

        return status.ToUpperInvariant() switch
        {
            "CONFIRMED" => "Confirmed",
            "CANCELLED" => "Cancelled",
            "TENTATIVE" => "Tentative",
            _ => status
        };
    }

    private static EventResponseStatus ParseParticipationStatus(string? status)
    {
        if (string.IsNullOrEmpty(status))
        {
            return EventResponseStatus.None;
        }

        return status.ToUpperInvariant() switch
        {
            "ACCEPTED" => EventResponseStatus.Accepted,
            "DECLINED" => EventResponseStatus.Declined,
            "TENTATIVE" => EventResponseStatus.Tentative,
            "NEEDS-ACTION" => EventResponseStatus.NeedsAction,
            _ => EventResponseStatus.None
        };
    }
}
