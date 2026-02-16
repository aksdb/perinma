using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using perinma.Models;
using perinma.Services;
using perinma.Storage;
using CalendarModel = perinma.Models.Calendar;

namespace perinma.Views.Calendar;

public partial class EventEditViewModel : ViewModelBase
{
    private readonly SqliteStorage _storage;
    private readonly Action<string> _onCompleted;
    private readonly CalendarEvent? _existingEvent;
    private readonly CalendarModel? _calendar;
    private readonly string? _existingRawEventData;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditMode))]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _location = string.Empty;

    [ObservableProperty]
    private DateTime _startTime;

    [ObservableProperty]
    private DateTime _endTime;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private CalendarModel? _selectedCalendar;

    [ObservableProperty]
    private TimeSpan _duration;

    partial void OnStartTimeChanged(DateTime value)
    {
        _endTime = value.Add(_duration);
        OnPropertyChanged(nameof(EndTime));
    }

    partial void OnEndTimeChanged(DateTime value)
    {
        if (value < _startTime)
        {
            _endTime = _startTime.Add(_duration);
            OnPropertyChanged(nameof(EndTime));
            OnPropertyChanged(nameof(StartTime));
        }
        else
        {
            _duration = _endTime - _startTime;
            OnPropertyChanged(nameof(Duration));
        }
    }

    partial void OnDurationChanged(TimeSpan value)
    {
        _endTime = _startTime.Add(_duration);
        OnPropertyChanged(nameof(EndTime));
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
                return _storage.GetCachedCalendars(_calendar.Account)
                    .Where(c => c.Enabled)
                    .OrderBy(c => c.Name);
            }
            else
            {
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
        Action<string> onCompleted)
    {
        _existingEvent = existingEvent;
        _calendar = calendar;
        _onCompleted = onCompleted;
        
        var storage = App.Services?.GetRequiredService<SqliteStorage>();
        if (storage == null)
        {
            throw new InvalidOperationException("SqliteStorage not available");
        }
        
        _storage = storage;
        _onCompleted = onCompleted;

        if (existingEvent != null && calendar != null)
        {
            Title = existingEvent.Title ?? string.Empty;
            Description = string.Empty;
            Location = string.Empty;
            _startTime = existingEvent.StartTime.ToDateTimeUnspecified();
            _endTime = existingEvent.EndTime.ToDateTimeUnspecified();
            _duration = _endTime - _startTime;
            SelectedCalendar = calendar;

            var rawDataTask = _storage.GetEventData(existingEvent.Reference.Id.ToString(), "rawData");
            _existingRawEventData = rawDataTask.GetAwaiter().GetResult();
        }
        else
        {
            var now = DateTime.Now;
            var rounded = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Local);
            _startTime = rounded;
            _duration = TimeSpan.FromMinutes(30);
            _endTime = _startTime.Add(_duration);
            SelectedCalendar = calendar;
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

            var targetCalendar = SelectedCalendar ?? _calendar;
            if (targetCalendar == null)
            {
                ErrorMessage = "Please select a calendar";
                return;
            }

            var accountId = targetCalendar.Account.Id.ToString();
            var calendarExternalId = targetCalendar.ExternalId ?? string.Empty;
            var providerService = App.Services?.GetRequiredService<SyncService>();
            var provider = providerService?.Providers?.GetValueOrDefault(targetCalendar.Account.Type);

            var startInstant = LocalDateTime.FromDateTime(StartTime).InZoneStrictly(DateTimeZoneProviders.Tzdb.GetSystemDefault()).ToInstant();
            var endInstant = LocalDateTime.FromDateTime(EndTime).InZoneStrictly(DateTimeZoneProviders.Tzdb.GetSystemDefault()).ToInstant();

            if (IsEditMode && _existingEvent != null && provider != null)
            {
                await provider.UpdateEventAsync(
                    accountId,
                    calendarExternalId,
                    _existingEvent.Reference.ExternalId ?? string.Empty,
                    Title,
                    string.IsNullOrWhiteSpace(Description) ? null : Description,
                    string.IsNullOrWhiteSpace(Location) ? null : Location,
                    startInstant,
                    endInstant,
                    _existingRawEventData);

                _onCompleted(_existingEvent.Reference.ExternalId ?? string.Empty);
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
                    startInstant,
                    endInstant,
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
            Console.WriteLine(ex);
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
