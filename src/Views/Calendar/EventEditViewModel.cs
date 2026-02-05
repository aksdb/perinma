using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using perinma.Models;
using perinma.Services;
using perinma.Storage;
using CalendarModel = perinma.Models.Calendar;

namespace perinma.Views.Calendar;

public partial class EventEditViewModel : ViewModelBase
{
    private readonly SqliteStorage _storage;
    private readonly IReadOnlyDictionary<AccountType, ICalendarProvider> _providers;
    private readonly Action<string> _onCompleted;
    private readonly CalendarEvent? _existingEvent;
    private readonly CalendarModel? _calendar;
    private readonly string? _existingRawEventData;

    private string _title = string.Empty;
    private string _description = string.Empty;
    private string _location = string.Empty;
    private DateTime _startTime;
    private DateTime _endTime;
    private bool _isSaving;
    private string _errorMessage = string.Empty;
    private CalendarModel? _selectedCalendar;

    public string Title
    {
        get => _title;
        set
        {
            if (SetProperty(ref _title, value))
            {
                OnPropertyChanged(nameof(IsEditMode));
            }
        }
    }

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public string Location
    {
        get => _location;
        set => SetProperty(ref _location, value);
    }

    public DateTime StartTime
    {
        get => _startTime;
        set
        {
            if (SetProperty(ref _startTime, value))
            {
                EndTime = value.AddMinutes(30);
                OnPropertyChanged(nameof(EndTime));
            }
        }
    }

    public DateTime EndTime
    {
        get => _endTime;
        set
        {
            if (value < _startTime)
            {
                _endTime = _startTime.AddMinutes(30);
                OnPropertyChanged();
            }
            else
            {
                SetProperty(ref _endTime, value);
            }
            OnPropertyChanged(nameof(StartTime));
        }
    }

    public bool IsSaving
    {
        get => _isSaving;
        set => SetProperty(ref _isSaving, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public CalendarModel? SelectedCalendar
    {
        get => _selectedCalendar;
        set => SetProperty(ref _selectedCalendar, value);
    }

    public bool IsEditMode => _existingEvent != null;

    public string WindowTitle => IsEditMode ? "Edit Event" : "New Event";

    public event EventHandler? RequestClose;

    public IEnumerable<CalendarModel> Calendars
    {
        get
        {
            if (_calendar != null)
            {
                // Edit mode: show calendars from same account as event
                return _storage.GetCachedCalendars(_calendar.Account)
                    .Where(c => c.Enabled)
                    .OrderBy(c => c.Name);
            }
            else
            {
                // Create mode: show all calendars from all accounts
                var allCalendars = new List<CalendarModel>();
                var accounts = _storage.GetCachedAccounts();

                foreach (var account in accounts)
                {
                    allCalendars.AddRange(_storage.GetCachedCalendars(account)
                        .Where(c => c.Enabled));
                }

                return allCalendars.OrderBy(c => c.Name);
            }
        }
    }

    public EventEditViewModel(
        CalendarEvent? existingEvent,
        CalendarModel? calendar,
        SqliteStorage storage,
        IReadOnlyDictionary<AccountType, ICalendarProvider> providers,
        Action<string> onCompleted)
    {
        _existingEvent = existingEvent;
        _calendar = calendar;
        _storage = storage;
        _providers = providers;
        _onCompleted = onCompleted;

        if (existingEvent != null && calendar != null)
        {
            // Edit mode
            Title = existingEvent.Title ?? string.Empty;
            Description = string.Empty;
            Location = string.Empty;
            _startTime = existingEvent.StartTime;
            _endTime = existingEvent.EndTime;
            SelectedCalendar = calendar;

            var rawDataTask = _storage.GetEventData(existingEvent.Id.ToString(), "rawData");
            _existingRawEventData = rawDataTask.GetAwaiter().GetResult();
        }
        else
        {
            // Create mode
            var now = DateTime.Now;
            var rounded = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Local);
            _startTime = rounded;
            SelectedCalendar = calendar; // May be null, will select first available
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsSaving)
            return;

        if (string.IsNullOrWhiteSpace(Title))
        {
            ErrorMessage = "Please enter a title";
            return;
        }

        try
        {
            IsSaving = true;
            ErrorMessage = string.Empty;

            // Use selected calendar instead of fixed calendar
            var targetCalendar = SelectedCalendar ?? _calendar;
            if (targetCalendar == null)
            {
                ErrorMessage = "Please select a calendar";
                return;
            }

            var accountId = targetCalendar.Account.Id.ToString();
            var calendarExternalId = targetCalendar.ExternalId ?? string.Empty;
            var provider = _providers.GetValueOrDefault(targetCalendar.Account.Type);

            if (IsEditMode && _existingEvent != null && provider != null)
            {
                await provider.UpdateEventAsync(
                    accountId,
                    calendarExternalId,
                    _existingEvent.ExternalId ?? string.Empty,
                    Title,
                    string.IsNullOrWhiteSpace(Description) ? null : Description,
                    string.IsNullOrWhiteSpace(Location) ? null : Location,
                    StartTime,
                    EndTime,
                    _existingRawEventData);

                _onCompleted(_existingEvent.ExternalId ?? string.Empty);
                RequestClose?.Invoke(this, EventArgs.Empty);
            }
            else if (provider != null)
            {
                var newEventId = await provider.CreateEventAsync(
                    accountId,
                    calendarExternalId,
                    Title,
                    string.IsNullOrWhiteSpace(Description) ? null : Description,
                    string.IsNullOrWhiteSpace(Location) ? null : Location,
                    StartTime,
                    EndTime,
                    null);

                _onCompleted(newEventId);
                RequestClose?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                ErrorMessage = "Calendar provider not available";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to save event: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _onCompleted(string.Empty);
        RequestClose?.Invoke(this, EventArgs.Empty);
    }
}
