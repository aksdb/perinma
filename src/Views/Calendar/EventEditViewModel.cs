using System;
using System.Collections.Generic;
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
using perinma.Views.Calendar.EventEdit;
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
    private bool _isSaving;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private CalendarModel? _selectedCalendar;

    public ObservableCollection<IEditableField> EditFields { get; } = [];

    private TitleEditViewModel? _titleField;
    private TimeRangeEditViewModel? _timeRangeField;
    private DescriptionEditViewModel? _descriptionField;
    private LocationEditViewModel? _locationField;

    partial void OnSelectedCalendarChanged(CalendarModel? value)
    {
        PopulateEditFields();
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
            SelectedCalendar = calendar;

            var rawDataTask = _storage.GetEventData(existingEvent.Reference.Id.ToString(), "rawData");
            _existingRawEventData = rawDataTask.GetAwaiter().GetResult();
        }
        else
        {
            SelectedCalendar = calendar;
        }

        PopulateEditFields();
    }

    private void PopulateEditFields()
    {
        EditFields.Clear();

        var targetCalendar = SelectedCalendar ?? _calendar;
        if (targetCalendar == null)
            return;

        var providerService = App.Services?.GetRequiredService<SyncService>();
        var provider = providerService?.Providers?.GetValueOrDefault(targetCalendar.Account.Type);
        if (provider == null)
            return;

        var supportedExtensions = provider.GetSupportedExtensions();

        _titleField = new TitleEditViewModel();
        if (_existingEvent != null && _existingEvent.Title != null)
            _titleField.Title = _existingEvent.Title;
        EditFields.Add(_titleField);

        _timeRangeField = new TimeRangeEditViewModel();
        if (_existingEvent != null)
        {
            _timeRangeField.StartTime = _existingEvent.StartTime.ToDateTimeUnspecified();
            _timeRangeField.EndTime = _existingEvent.EndTime.ToDateTimeUnspecified();
        }
        EditFields.Add(_timeRangeField);

        if (supportedExtensions.Contains(CalendarEventExtensions.Description))
        {
            var existingDescription = _existingEvent?.Extensions.Get(CalendarEventExtensions.Description);
            _descriptionField = new DescriptionEditViewModel(existingDescription);
            EditFields.Add(_descriptionField);
        }

        if (supportedExtensions.Contains(CalendarEventExtensions.Location))
        {
            var existingLocation = _existingEvent?.Extensions.Get(CalendarEventExtensions.Location);
            _locationField = new LocationEditViewModel(existingLocation);
            EditFields.Add(_locationField);
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsSaving)
            return;

        if (_titleField == null || string.IsNullOrWhiteSpace(_titleField.Title))
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
            var provider = App.Services?.GetRequiredService<SyncService>()?.Providers?.GetValueOrDefault(targetCalendar.Account.Type);

            var startInstant = LocalDateTime.FromDateTime(_timeRangeField!.StartTime).InZoneStrictly(DateTimeZoneProviders.Tzdb.GetSystemDefault()).ToInstant();
            var endInstant = LocalDateTime.FromDateTime(_timeRangeField.EndTime).InZoneStrictly(DateTimeZoneProviders.Tzdb.GetSystemDefault()).ToInstant();

            var extensions = new ModelExtensions();

            if (_descriptionField != null)
            {
                var richText = _descriptionField.GetRichText();
                if (richText != null)
                    extensions.Set(CalendarEventExtensions.Description, richText);
            }

            if (_locationField != null && !string.IsNullOrWhiteSpace(_locationField.Location))
                extensions.Set(CalendarEventExtensions.Location, _locationField.Location);

            if (IsEditMode && _existingEvent != null && provider != null)
            {
                await provider.UpdateEventAsync(
                    accountId,
                    calendarExternalId,
                    _existingEvent.Reference.ExternalId ?? string.Empty,
                    _titleField.Title,
                    extensions,
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
                    _titleField.Title,
                    extensions,
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
