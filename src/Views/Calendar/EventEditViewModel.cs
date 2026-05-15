using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using NodaTime.Extensions;
using perinma.Messaging;
using perinma.Models;
using perinma.Services;
using perinma.Storage;
using perinma.Storage.Models;
using perinma.Utils;
using perinma.Views.Calendar.EventEdit;
using CalendarModel = perinma.Models.Calendar;

namespace perinma.Views.Calendar;

public partial class EventEditViewModel : ViewModelBase
{
    private readonly SqliteStorage _storage;
    private readonly Action<EventEditResult> _onCompleted;
    private readonly CalendarEvent? _existingEvent;
    private readonly CalendarModel? _calendar;
    private readonly string? _existingRawEventData;
    private readonly Window? _ownerWindow;

    [ObservableProperty]
    public partial bool IsSaving { get; set; }

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    private CalendarModel? _selectedCalendar;

    public TitleEditViewModel? TitleField { get; private set; }
    public ObservableCollection<FieldRow> FieldRows { get; } = [];

    private TitleEditViewModel? _titleField;
    private TimeRangeEditViewModel? _timeRangeField;
    private DescriptionEditViewModel? _descriptionField;
    private LocationEditViewModel? _locationField;
    private ParticipantsEditViewModel? _participantsField;
    private ReminderEditViewModel? _reminderField;
    private SendInvitesResult? _sendInvitesResult;

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
            var calendarSource = App.Services?.GetRequiredService<ICalendarSource>();
            if (calendarSource == null)
            {
                return Enumerable.Empty<CalendarModel>();
            }

            if (_calendar != null)
            {
                return calendarSource.GetCalendars(_calendar.Account)
                    .Where(c => c.Enabled)
                    .OrderBy(c => c.Name);
            }

            var allCalendars = new List<CalendarModel>();
            var accounts = _storage.GetCachedAccounts();

            foreach (var account in accounts)
            {
                allCalendars.AddRange(calendarSource.GetCalendars(account)
                    .Where(c => c.Enabled));
            }

            return allCalendars.OrderBy(c => c.Name);
        }
    }

    private readonly DateTime? _initialStartTime;
    private readonly DateTime? _initialEndTime;
    private readonly bool _initialFullDay;

    public EventEditViewModel(
        Window? ownerWindow,
        CalendarEvent? existingEvent,
        CalendarModel? calendar,
        Action<EventEditResult> onCompleted,
        DateTime? initialStartTime = null,
        DateTime? initialEndTime = null,
        bool isFullDay = false)
    {
        _ownerWindow = ownerWindow ?? App.MainWindow;
        _existingEvent = existingEvent;
        _calendar = calendar;
        _onCompleted = onCompleted;
        _initialStartTime = initialStartTime;
        _initialEndTime = initialEndTime;
        _initialFullDay = isFullDay;

        var storage = App.Services?.GetRequiredService<SqliteStorage>();

        _storage = storage ?? throw new InvalidOperationException("SqliteStorage not available");

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
        FieldRows.Clear();

        var targetCalendar = SelectedCalendar ?? _calendar;
        if (targetCalendar == null)
            return;

        var providerService = App.Services?.GetRequiredService<SyncService>();
        var provider = providerService?.Providers?.GetValueOrDefault(targetCalendar.Account.Type);
        if (provider == null)
            return;

        var supportedExtensions = provider.GetSupportedExtensions();

        _titleField = new TitleEditViewModel();
        if (_existingEvent is { Title: not null })
            _titleField.Title = _existingEvent.Title;
        TitleField = _titleField;
        OnPropertyChanged(nameof(TitleField));

        _timeRangeField = new TimeRangeEditViewModel
        {
            IsFullDaySupported = supportedExtensions.Contains(CalendarEventExtensions.FullDay)
        };
        bool timeExpanded = true;
        if (_existingEvent != null)
        {
            _timeRangeField.StartTime = _existingEvent.StartTime.ToDateTimeUnspecified();
            _timeRangeField.EndTime = _existingEvent.EndTime.ToDateTimeUnspecified();
            var isFullDay = _existingEvent.Extensions.Get(CalendarEventExtensions.FullDay);
            _timeRangeField.IsFullDay = isFullDay;
            if (isFullDay)
                _timeRangeField.EndDate = _timeRangeField.EndDate.AddDays(-1);
            timeExpanded = false;
        }
        else if (_initialStartTime.HasValue && _initialEndTime.HasValue)
        {
            _timeRangeField.StartTime = _initialStartTime.Value;
            _timeRangeField.EndTime = _initialEndTime.Value;
            _timeRangeField.IsFullDay = _initialFullDay;
        }

        FieldRows.Add(new FieldRow(_timeRangeField, "📅", startExpanded: timeExpanded));

        _reminderField = new ReminderEditViewModel();
        bool reminderExpanded = false;
        if (_existingEvent != null && _existingRawEventData != null)
        {
            var existingReminders = provider.GetReminderMinutes(_existingRawEventData);
            if (existingReminders.Count > 0)
            {
                _reminderField = new ReminderEditViewModel(existingReminders[0]);
                reminderExpanded = true;
            }
        }

        FieldRows.Add(new FieldRow(_reminderField, "🔔", startExpanded: reminderExpanded));

        if (supportedExtensions.Contains(CalendarEventExtensions.Description))
        {
            var existingDescription = _existingEvent?.Extensions.Get(CalendarEventExtensions.Description);
            _descriptionField = new DescriptionEditViewModel(existingDescription);
            FieldRows.Add(new FieldRow(_descriptionField, "📝", startExpanded: _descriptionField.HasValue));
        }

        if (supportedExtensions.Contains(CalendarEventExtensions.Location))
        {
            var existingLocation = _existingEvent?.Extensions.Get(CalendarEventExtensions.Location);
            _locationField = new LocationEditViewModel(existingLocation);
            FieldRows.Add(new FieldRow(_locationField, "📍", startExpanded: _locationField.HasValue));
        }

        if (supportedExtensions.Contains(CalendarEventExtensions.Participants))
        {
            var existingParticipants = _existingEvent?.Extensions.Get(CalendarEventExtensions.Participants);
            _participantsField = existingParticipants is { Count: > 0 }
                ? new ParticipantsEditViewModel(_storage, existingParticipants)
                : new ParticipantsEditViewModel(_storage);
            FieldRows.Add(new FieldRow(_participantsField, "👥", startExpanded: _participantsField.HasValue));
            _participantsField.SelectedParticipants.CollectionChanged += (_, _) =>
                CheckAvailabilityCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsSaving)
            return;

        if (_titleField == null || _timeRangeField == null || _reminderField == null ||
            string.IsNullOrWhiteSpace(_titleField.Title))
        {
            ErrorMessage = "Please enter a title";
            return;
        }

        // Show invite dialog if there are participants
        if (_participantsField is { SelectedParticipants.Count: > 0 })
        {
            var dialogViewModel = new SendInvitesDialogViewModel();
            var dialog = new SendInvitesDialog { DataContext = dialogViewModel };

            _sendInvitesResult = await dialog.ShowDialog<SendInvitesResult>(_ownerWindow);
        }
        else
        {
            _sendInvitesResult = SendInvitesResult.SendToNone;
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
            var provider = App.Services?.GetRequiredService<SyncService>()?.Providers
                ?.GetValueOrDefault(targetCalendar.Account.Type);

            var extensions = new ModelExtensions();

            LocalDateTime eventStartTime = _timeRangeField.StartTime.ToLocalDateTime();
            LocalDateTime eventEndTime = _timeRangeField.EndTime.ToLocalDateTime();

            if (_timeRangeField.IsFullDay)
            {
                extensions.Set(CalendarEventExtensions.FullDay, true);
                eventStartTime = eventStartTime.Date.AtMidnight();
                eventEndTime = eventEndTime.Date.PlusDays(1).AtMidnight();
            }

            if (_descriptionField != null)
            {
                var richText = _descriptionField.GetRichText();
                if (richText != null)
                    extensions.Set(CalendarEventExtensions.Description, richText);
            }

            if (_locationField != null && !string.IsNullOrWhiteSpace(_locationField.Location))
                extensions.Set(CalendarEventExtensions.Location, _locationField.Location);

            if (_participantsField != null)
            {
                var participants = _participantsField.GetParticipants();
                if (participants.Count > 0)
                {
                    extensions.Set(CalendarEventExtensions.Participants, participants);
                }
            }

            // Add reminder to extensions if enabled
            if (_reminderField != null && _reminderField.HasReminder && _reminderField.ReminderMinutes >= 0)
            {
                extensions.Set(CalendarEventExtensions.ReminderMinutesBefore, _reminderField.ReminderMinutes);
            }

            if (IsEditMode && _existingEvent != null && provider != null)
            {
                var updatedExtensions = _existingEvent.Extensions;

                updatedExtensions.Set(CalendarEventExtensions.FullDay, _timeRangeField.IsFullDay);

                _descriptionField?.GetRichText()?.Let(richText =>
                    updatedExtensions.Set(CalendarEventExtensions.Description, richText));

                _locationField?.Let(location =>
                {
                    if (!string.IsNullOrWhiteSpace(location.Location))
                        updatedExtensions.Set(CalendarEventExtensions.Location, location.Location);
                });

                _participantsField?.Let(participants =>
                {
                    var participantList = participants.GetParticipants();
                    if (participantList.Count > 0)
                    {
                        updatedExtensions.Set(CalendarEventExtensions.Participants, participantList);
                    }
                });

                // Add reminder to extensions if enabled
                _reminderField?.Let(reminder =>
                {
                    if (reminder.HasReminder && reminder.ReminderMinutes >= 0)
                        updatedExtensions.Set(CalendarEventExtensions.ReminderMinutesBefore, reminder.ReminderMinutes);
                });

                var updatedEvent = new CalendarEvent
                {
                    Reference = _existingEvent.Reference,
                    StartTime = eventStartTime,
                    EndTime = eventEndTime,
                    Title = _titleField.Title,
                    Extensions = updatedExtensions
                };

                var rawData =
                    await provider.UpdateEventAsync(updatedEvent, _sendInvitesResult ?? SendInvitesResult.SendToNone);

                var calendarId = targetCalendar.Id.ToString();
                var changedAt = SystemClock.Instance.GetCurrentInstant().ToUnixTimeSeconds();

                var eventDbo = new CalendarEventDbo
                {
                    CalendarId = calendarId,
                    ExternalId = _existingEvent.Reference.ExternalId,
                    StartTime = eventStartTime.ToInstant().ToUnixTimeSeconds(),
                    EndTime = eventEndTime.ToInstant().ToUnixTimeSeconds(),
                    Title = _titleField.Title,
                    ChangedAt = changedAt
                };

                var eventId = await _storage.CreateOrUpdateEventAsync(eventDbo);
                switch (rawData)
                {
                    case DataAttribute.Text text:
                        await _storage.SetEventData(eventId, "rawData", text.value);
                        break;
                    case DataAttribute.JsonText jsonText:
                        await _storage.SetEventDataJson(eventId, "rawData", jsonText.value);
                        break;
                    default:
                        throw new InvalidOperationException("Unknown rawData type.");
                }

                // Populate reminders for the updated event
                var reminderService = App.Services?.GetRequiredService<ReminderService>();
                if (reminderService != null)
                {
                    await reminderService.PopulateRemindersForEventAsync(eventId, calendarId,
                        targetCalendar.Account.Type);
                }

                WeakReferenceMessenger.Default.Send(new EventsChangedMessage());

                RequestClose?.Invoke(this, EventArgs.Empty);
            }
            else if (provider != null)
            {
                var (newEventId, rawData) = await provider.CreateEventAsync(
                    accountId,
                    calendarExternalId,
                    _titleField.Title,
                    extensions,
                    eventStartTime,
                    eventEndTime,
                    _sendInvitesResult ?? SendInvitesResult.SendToNone);

                var calendarId = targetCalendar.Id.ToString();
                var changedAt = SystemClock.Instance.GetCurrentInstant().ToUnixTimeSeconds();

                var eventDbo = new CalendarEventDbo
                {
                    CalendarId = calendarId,
                    ExternalId = newEventId,
                    StartTime = eventStartTime.ToInstant().ToUnixTimeSeconds(),
                    EndTime = eventEndTime.ToInstant().ToUnixTimeSeconds(),
                    Title = _titleField.Title,
                    ChangedAt = changedAt
                };

                var eventId = await _storage.CreateOrUpdateEventAsync(eventDbo);
                await _storage.SetEventData(eventId, "rawData", rawData);

                // Populate reminders for the newly created event
                var reminderService = App.Services?.GetService<ReminderService>();
                if (reminderService != null)
                {
                    await reminderService.PopulateRemindersForEventAsync(eventId, calendarId,
                        targetCalendar.Account.Type);
                }

                WeakReferenceMessenger.Default.Send(new EventsChangedMessage());

                _onCompleted(new EventEditResult.Success(eventId));

                RequestClose?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                ErrorMessage = "Calendar provider not available";
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            _onCompleted(new EventEditResult.Error(ex));
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCheckAvailability))]
    private async Task CheckAvailabilityAsync()
    {
        if (_participantsField == null || _timeRangeField == null || SelectedCalendar == null)
            return;

        var emails = _participantsField.SelectedParticipants
            .Select(p => p.Email)
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .ToList();

        if (emails.Count == 0) return;

        var provider = App.Services?.GetRequiredService<SyncService>()?.Providers
            ?.GetValueOrDefault(SelectedCalendar.Account.Type);
        if (provider == null) return;

        var accountId = SelectedCalendar.Account.Id.ToString();

        // Own-events delegate: uses ICalendarSource so recurrence expansion, all enabled
        // calendars, and provider-specific parsing (including NonBlocking) are handled
        // consistently with the calendar views.
        var calendarSource = App.Services?.GetService<ICalendarSource>();
        Func<Interval, CancellationToken, Task<IList<OwnCalendarEvent>>>? getOwnEvents = null;
        if (calendarSource != null)
        {
            getOwnEvents = (interval, ct) => Task.Run(() =>
                (IList<OwnCalendarEvent>)calendarSource
                    .GetCalendarEvents(interval)
                    .Where(e => !e.Extensions.Get(CalendarEventExtensions.NonBlocking)
                                && !e.Reference.Calendar.Extensions.Get(CalendarExtensions.IsReadOnly))
                    .Select(e => new OwnCalendarEvent
                    {
                        Title = string.IsNullOrWhiteSpace(e.Title) ? "(No title)" : e.Title,
                        Start = e.StartTime.ToInstant(),
                        End = e.EndTime.ToInstant(),
                        CalendarColor = e.Reference.Calendar.Color,
                        CalendarName = e.Reference.Calendar.Name
                    })
                    .ToList(), ct);
        }

        var vm = new Availability.AvailabilityWindowViewModel(
            provider, accountId, emails,
            _timeRangeField.StartTime, _timeRangeField.EndTime,
            getOwnEvents: getOwnEvents);

        var dialog = new Availability.AvailabilityWindow { DataContext = vm };
        var result = await dialog.ShowDialog<(DateTime, DateTime)?>(_ownerWindow);

        if (result is { } slot)
        {
            _timeRangeField.StartTime = slot.Item1;
            _timeRangeField.EndTime = slot.Item2;
        }
    }

    private bool CanCheckAvailability() =>
        _participantsField is { SelectedParticipants.Count: > 0 } && SelectedCalendar != null;


    [RelayCommand]
    private void Cancel()
    {
        _onCompleted(new EventEditResult.Cancelled());
        RequestClose?.Invoke(this, EventArgs.Empty);
    }
}