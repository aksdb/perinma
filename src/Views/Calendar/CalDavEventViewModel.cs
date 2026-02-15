using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using perinma.Models;
using perinma.Services;
using perinma.Storage;
using perinma.Storage.Models;
using ICalCalendar = Ical.Net.Calendar;

namespace perinma.Views.Calendar;

public partial class CalDavEventViewModel : ViewModelBase, IRespondableEventViewModel
{
    public CalendarEvent CalendarEvent => _calendarEvent;

    private readonly CalendarEvent _calendarEvent;
    private readonly ICalendarProvider? _calendarProvider;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _location = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOrganizer))]
    private AttendeeViewModel? _organizer;

    public bool HasOrganizer => Organizer != null;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private string _categories = string.Empty;

    [ObservableProperty]
    private string _url = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isUpdating;

    [ObservableProperty]
    private bool _canRespond;

    [ObservableProperty]
    private EventResponseStatus _currentResponseStatus = EventResponseStatus.None;

    public ObservableCollection<AttendeeViewModel> Attendees { get; } = [];

    public CalDavEventViewModel(
        CalendarEvent calendarEvent,
        ICalendarProvider? calendarProvider = null)
    {
        _calendarEvent = calendarEvent;
        _calendarProvider = calendarProvider;
        _isLoading = true;
        _ = LoadEventDataAsync();
    }

    private async Task LoadEventDataAsync()
    {
        try
        {
            var storage = App.Services?.GetRequiredService<SqliteStorage>();
            if (storage == null) return;
            
            var rawData = await storage.GetEventData(_calendarEvent.Reference.Id.ToString(), "rawData");
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
                        var organizerEmail = ExtractEmailFromUri(iCalEvent.Organizer.Value?.ToString());
                        var organizerName = !string.IsNullOrEmpty(iCalEvent.Organizer.CommonName)
                            ? iCalEvent.Organizer.CommonName
                            : organizerEmail;
                        Organizer = await CreateContactViewModelAsync(organizerName, organizerEmail);
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
                        await ExtractAttendeesAsync(iCalEvent);

                        // Check if user can respond (is an attendee)
                        CanRespond = Attendees.Count > 0;
                        CurrentResponseStatus = _calendarEvent.ResponseStatus;
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

    private async Task ExtractAttendeesAsync(Ical.Net.CalendarComponents.CalendarEvent iCalEvent)
    {
        if (iCalEvent.Attendees == null)
            return;

        var storage = App.Services?.GetRequiredService<SqliteStorage>();
        if (storage == null) return;

        foreach (var attendee in iCalEvent.Attendees)
        {
            var email = ExtractEmailFromUri(attendee.Value?.ToString());
            var name = !string.IsNullOrEmpty(attendee.CommonName)
                ? attendee.CommonName
                : email;

            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var responseStatus = ParseParticipationStatus(attendee.ParticipationStatus);

            var attendeeVm = AttendeeViewModel.Create(
                name,
                email,
                responseStatus,
                isOrganizer: false);

            // Try to enrich with contact data
            if (!string.IsNullOrEmpty(email))
            {
                var contact = await storage.GetContactByEmailAsync(email);
                if (contact != null)
                {
                    attendeeVm.EnrichWithContact(contact);
                }
            }

            Attendees.Add(attendeeVm);
        }

        // Load photos in background
        _ = LoadAttendeePhotosAsync();
    }

    private async Task LoadAttendeePhotosAsync()
    {
        await Parallel.ForEachAsync(Attendees,
            new ParallelOptions { MaxDegreeOfParallelism = 3 },
            async (attendee, cancellationToken) =>
            {
                await attendee.LoadPhotoAsync(cancellationToken);
            });
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

    /// <summary>
    /// Creates an AttendeeViewModel for organizer with contact enrichment
    /// </summary>
    private async Task<AttendeeViewModel?> CreateContactViewModelAsync(string? displayName, string? email)
    {
        if (string.IsNullOrEmpty(displayName) && string.IsNullOrEmpty(email))
            return null;

        var storage = App.Services?.GetRequiredService<SqliteStorage>();
        if (storage == null) return null;

        var name = displayName ?? email ?? string.Empty;
        var vm = AttendeeViewModel.Create(name, email, EventResponseStatus.None, isOrganizer: false);

        // Try to enrich with contact data
        if (!string.IsNullOrEmpty(email))
        {
            var contact = await storage.GetContactByEmailAsync(email);
            if (contact != null)
            {
                vm.EnrichWithContact(contact);
                _ = vm.LoadPhotoAsync();
            }
        }

        return vm;
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

            var accountId = _calendarEvent.Reference.Calendar.Account.Id.ToString();
            var calendarId = _calendarEvent.Reference.Calendar.ExternalId;
            var eventId = _calendarEvent.Reference.ExternalId;

            if (string.IsNullOrEmpty(calendarId) || string.IsNullOrEmpty(eventId))
            {
                Console.WriteLine("Missing calendar or event ID");
                return;
            }

            // Get raw event data for provider
            var storage = App.Services?.GetRequiredService<SqliteStorage>();
            if (storage == null) return;
            
            var rawData = await storage.GetEventData(_calendarEvent.Reference.Id.ToString(), "rawData");
            if (string.IsNullOrEmpty(rawData))
            {
                Console.WriteLine("Failed to get raw event data");
                return;
            }

            // Use the provider to respond to event
            await _calendarProvider.RespondToEventAsync(accountId, calendarId, eventId, rawData, responseStatus);

            // Update local state
            CurrentResponseStatus = responseStatus switch
            {
                "accepted" => EventResponseStatus.Accepted,
                "declined" => EventResponseStatus.Declined,
                "tentative" => EventResponseStatus.Tentative,
                _ => EventResponseStatus.None
            };
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
