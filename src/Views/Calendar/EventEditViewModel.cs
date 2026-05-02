using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
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

    private readonly DateTime? _initialStartTime;
    private readonly DateTime? _initialEndTime;
    private readonly bool _initialFullDay;

    public EventEditViewModel(
        CalendarEvent? existingEvent,
        CalendarModel? calendar,
        Action<EventEditResult> onCompleted,
        DateTime? initialStartTime = null,
        DateTime? initialEndTime = null,
        bool isFullDay = false)
    {
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
        if (_existingEvent is { Title: not null })
            _titleField.Title = _existingEvent.Title;
        EditFields.Add(_titleField);

        _timeRangeField = new TimeRangeEditViewModel
        {
            IsFullDaySupported = supportedExtensions.Contains(CalendarEventExtensions.FullDay)
        };
        if (_existingEvent != null)
        {
            _timeRangeField.StartTime = _existingEvent.StartTime.ToDateTimeUnspecified();
            _timeRangeField.EndTime = _existingEvent.EndTime.ToDateTimeUnspecified();
            var isFullDay = _existingEvent.Extensions.Get(CalendarEventExtensions.FullDay);
            _timeRangeField.IsFullDay = isFullDay;
            if (isFullDay)
                _timeRangeField.EndDate = _timeRangeField.EndDate.AddDays(-1);
        }
        else if (_initialStartTime.HasValue && _initialEndTime.HasValue)
        {
            _timeRangeField.StartTime = _initialStartTime.Value;
            _timeRangeField.EndTime = _initialEndTime.Value;
            _timeRangeField.IsFullDay = _initialFullDay;
        }

        EditFields.Add(_timeRangeField);

        // Add reminder field
        _reminderField = new ReminderEditViewModel();
        if (_existingEvent != null && _existingRawEventData != null)
        {
            var existingReminders = provider.GetReminderMinutes(_existingRawEventData);
            if (existingReminders.Count > 0)
            {
                _reminderField = new ReminderEditViewModel(existingReminders[0]);
            }
        }
        EditFields.Add(_reminderField);

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

        // Add participants field if supported
        if (supportedExtensions.Contains(CalendarEventExtensions.Participants))
        {
            var existingParticipants = _existingEvent?.Extensions.Get(CalendarEventExtensions.Participants);
            if (existingParticipants != null && existingParticipants.Count > 0)
            {
                _participantsField = new ParticipantsEditViewModel(_storage, existingParticipants);
            }
            else
            {
                _participantsField = new ParticipantsEditViewModel(_storage);
            }
            EditFields.Add(_participantsField);
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsSaving)
            return;

        if (_titleField == null || _timeRangeField == null || _reminderField == null || string.IsNullOrWhiteSpace(_titleField.Title))
        {
            ErrorMessage = "Please enter a title";
            return;
        }

        // Show invite dialog if there are participants
        if (_participantsField != null && _participantsField.SelectedParticipants.Count > 0)
        {
            var dialogViewModel = new SendInvitesDialogViewModel();
            var dialog = new SendInvitesDialog { DataContext = dialogViewModel };

            var result = await dialog.ShowDialog<SendInvitesResult>(this as Window);
            if (result == null)
                return;

            _sendInvitesResult = result;
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

                var rawData = await provider.UpdateEventAsync(updatedEvent, _sendInvitesResult ?? SendInvitesResult.SendToNone);

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
                    await reminderService.PopulateRemindersForEventAsync(eventId, calendarId, targetCalendar.Account.Type);
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
                    await reminderService.PopulateRemindersForEventAsync(eventId, calendarId, targetCalendar.Account.Type);
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

    [RelayCommand]
    private void Cancel()
    {
        _onCompleted(new EventEditResult.Cancelled());
        RequestClose?.Invoke(this, EventArgs.Empty);
    }
}