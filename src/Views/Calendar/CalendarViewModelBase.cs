using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using perinma.Messaging;
using perinma.Models;
using perinma.Services;
using perinma.Storage;
using perinma.Views.MessageBox;
using perinma.Views.Calendar.EventEdit;
using perinma.Views.Reminders;

namespace perinma.Views.Calendar;

public abstract partial class CalendarViewModelBase : ViewModelBase
{
    protected readonly ICalendarSource _calendarSource;
    protected readonly SqliteStorage _storage;

    public SettingsService? SettingsService { get; }
    public DebugFeaturesService DebugFeatures { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DateRangeDisplay))]
    private DateTime _viewStart = DateTime.Today;

    public DateTime? HighlightStart { get; protected set; }
    public DateTime? HighlightEnd { get; protected set; }

    public abstract string DateRangeDisplay { get; }

    protected CalendarViewModelBase(
        ICalendarSource calendarSource,
        SettingsService? settingsService = null,
        DebugFeaturesService? debugFeatures = null)

    {
        _calendarSource = calendarSource;
        SettingsService = settingsService;
        DebugFeatures = debugFeatures ?? App.Services?.GetService<DebugFeaturesService>() ?? new DebugFeaturesService();

        var storage = App.Services?.GetRequiredService<SqliteStorage>();
        if (storage == null)
        {
            throw new InvalidOperationException("SqliteStorage not available");
        }

        _storage = storage;
    }


    partial void OnViewStartChanged(DateTime value)
    {
        OnViewStartDateChanged(value);
    }

    protected virtual void OnViewStartDateChanged(DateTime value) { }

    protected abstract void PerformNavigationNext();
    protected abstract void PerformNavigationPrevious();
    protected abstract void PerformNavigationToday();

    [RelayCommand]
    private void Next() => PerformNavigationNext();

    [RelayCommand]
    private void Previous() => PerformNavigationPrevious();

    [RelayCommand]
    private void Today() => PerformNavigationToday();

    [RelayCommand]
    private async Task EditEventAsync(CalendarEvent? eventToEdit)
    {
        if (eventToEdit == null) return;
        OpenEventEditor(eventToEdit);
    }

    [RelayCommand]
    private async Task DeleteEventAsync(CalendarEvent? eventToDelete)
    {
        if (eventToDelete == null) return;

        try
        {
            var syncService = App.Services?.GetRequiredService<SyncService>();
            if (syncService == null)
            {
                throw new InvalidOperationException("SyncService not available");
            }

            if (!syncService.Providers.TryGetValue(eventToDelete.Reference.Calendar.Account.Type, out var provider))
            {
                throw new InvalidOperationException(
                    $"No provider found for account type {eventToDelete.Reference.Calendar.Account.Type}");
            }

            var deleteAction = await ChooseDeleteActionAsync(eventToDelete);
            if (!deleteAction.HasValue)
                return;

            await provider.DeleteEventAsync(eventToDelete, deleteAction.Value);
            await syncService.RefreshCalendarAsync(eventToDelete.Reference.Calendar.Id.ToString());
            WeakReferenceMessenger.Default.Send(new EventsChangedMessage());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to delete event: {ex}");
            throw;
        }
    }

    [RelayCommand]
    private async Task TriggerReminderAsync(CalendarEvent? eventToTrigger)
    {
        if (!DebugFeatures.IsDebuggingEnabled || eventToTrigger == null)
            return;

        var reminderService = App.Services?.GetRequiredService<ReminderService>();
        if (reminderService == null)
            throw new InvalidOperationException("ReminderService not available");

        var result = await reminderService.TriggerRemindersNowAsync([eventToTrigger.Reference.Id.ToString()]);
        if (result.Reminders.Count == 0)
            return;

        var ownerWindow = App.MainWindow
            ?? throw new InvalidOperationException("MainWindow not available");
        await ReminderNotificationWindow.ShowAsync(ownerWindow, reminderService, result.Reminders.ToList());
    }

    public async void OpenEventEditor(CalendarEvent? existingEvent = null,
        DateTime? initialStartTime = null,
        DateTime? initialEndTime = null,
        bool isFullDay = false)
    {
        var eventToEdit = existingEvent;
        var editScope = EventEditScope.Event;

        if (existingEvent != null)
        {
            var selectedScope = await ChooseEditScopeAsync(existingEvent);
            if (!selectedScope.HasValue)
                return;

            editScope = selectedScope.Value;
            if (editScope == EventEditScope.Series)
                eventToEdit = await ResolveSeriesEventForEditAsync(existingEvent);
        }

        var onCompleted = new Action<EventEditResult>(async result =>
        {
            switch (result)
            {
                case EventEditResult.Error error:
                    Console.WriteLine($"Event saving failed: {error.Exception}");

                    await MessageBoxWindow.ShowAsync(
                        null,
                        "Error",
                        $"Failed to save event: {error.Exception.Message}",
                        MessageBoxType.Error,
                        MessageBoxButtons.Ok);
                    break;
                case EventEditResult.Success:
                    Load();
                    break;
            }
        });

        var editor = new EventEditView
        {
            DataContext = new EventEditViewModel(
                ownerWindow: App.MainWindow,
                existingEvent: eventToEdit,
                calendar: eventToEdit?.Reference.Calendar,
                onCompleted: onCompleted,
                editScope: editScope,
                initialStartTime: initialStartTime,
                initialEndTime: initialEndTime,
                isFullDay: isFullDay
            )
        };
        editor.Show();
    }

    private async Task<EventEditScope?> ChooseEditScopeAsync(CalendarEvent calendarEvent)
    {
        var recurrenceEdit = calendarEvent.Extensions.Get(CalendarEventExtensions.RecurrenceEdit)
            ?? new RecurrenceEditInfo { Kind = RecurrenceEditKind.None, AllowedActions = [] };
        if (!recurrenceEdit.AllowedActions.Contains(RecurringEventAction.EditOccurrence) &&
            !recurrenceEdit.AllowedActions.Contains(RecurringEventAction.EditSeries))
        {
            return EventEditScope.Event;
        }

        var options = new List<RecurrenceActionOption>();
        if (recurrenceEdit.AllowedActions.Contains(RecurringEventAction.EditOccurrence))
            options.Add(new RecurrenceActionOption("Edit this occurrence", RecurringEventAction.EditOccurrence));
        if (recurrenceEdit.AllowedActions.Contains(RecurringEventAction.EditSeries))
            options.Add(new RecurrenceActionOption("Edit entire series", RecurringEventAction.EditSeries));
        if (options.Count == 1)
            return options[0].Action == RecurringEventAction.EditSeries ? EventEditScope.Series : EventEditScope.Occurrence;

        var action = await ShowRecurrenceActionDialogAsync(
            "Edit Recurring Event",
            calendarEvent.Title ?? "Choose what to edit",
            options);

        return action switch
        {
            RecurringEventAction.EditOccurrence => EventEditScope.Occurrence,
            RecurringEventAction.EditSeries => EventEditScope.Series,
            null => null,
            _ => throw new InvalidOperationException($"Unsupported edit action {action}")
        };
    }

    private async Task<EventDeleteAction?> ChooseDeleteActionAsync(CalendarEvent calendarEvent)
    {
        var recurrenceEdit = calendarEvent.Extensions.Get(CalendarEventExtensions.RecurrenceEdit)
            ?? new RecurrenceEditInfo { Kind = RecurrenceEditKind.None, AllowedActions = [] };
        var title = calendarEvent.Title ?? "[no title]";

        var options = new List<RecurrenceActionOption>();
        if (recurrenceEdit.AllowedActions.Contains(RecurringEventAction.DeleteOccurrence))
            options.Add(new RecurrenceActionOption("Delete this occurrence", RecurringEventAction.DeleteOccurrence));
        if (recurrenceEdit.AllowedActions.Contains(RecurringEventAction.DeleteSeries))
            options.Add(new RecurrenceActionOption("Delete entire series", RecurringEventAction.DeleteSeries));
        if (recurrenceEdit.AllowedActions.Contains(RecurringEventAction.RevertOverride))
            options.Add(new RecurrenceActionOption("Revert this occurrence to the series", RecurringEventAction.RevertOverride));

        if (options.Count == 0)
        {
            var result = await MessageBoxWindow.ShowAsync(
                null,
                "Delete Event",
                $"Are you sure you want to delete \"{title}\"?",
                MessageBoxType.Warning,
                MessageBoxButtons.YesNo);
            return result == MessageBoxResult.Yes ? EventDeleteAction.Event : null;
        }

        var action = await ShowRecurrenceActionDialogAsync(
            "Delete Recurring Event",
            title,
            options);

        return action switch
        {
            RecurringEventAction.DeleteOccurrence => EventDeleteAction.Occurrence,
            RecurringEventAction.DeleteSeries => EventDeleteAction.Series,
            RecurringEventAction.RevertOverride => EventDeleteAction.RevertOverride,
            null => null,
            _ => throw new InvalidOperationException($"Unsupported delete action {action}")
        };
    }

    private async Task<CalendarEvent> ResolveSeriesEventForEditAsync(CalendarEvent calendarEvent)
    {
        var syncService = App.Services?.GetRequiredService<SyncService>()
                          ?? throw new InvalidOperationException("SyncService not available");
        if (!syncService.Providers.TryGetValue(calendarEvent.Reference.Calendar.Account.Type, out var provider))
            throw new InvalidOperationException($"No provider found for account type {calendarEvent.Reference.Calendar.Account.Type}");

        var recurrenceEdit = calendarEvent.Extensions.Get(CalendarEventExtensions.RecurrenceEdit)
            ?? new RecurrenceEditInfo { Kind = RecurrenceEditKind.None, AllowedActions = [] };
        var targetExternalId = recurrenceEdit.SeriesExternalId ?? calendarEvent.Reference.ExternalId
            ?? throw new InvalidOperationException("Series event id is missing");
        var storedEvent = await _storage.GetEventByExternalIdAsync(calendarEvent.Reference.Calendar.Id.ToString(), targetExternalId)
            ?? throw new InvalidOperationException("Series event not found in local storage");
        var rawData = await _storage.GetEventData(storedEvent.EventId, "rawData")
                      ?? throw new InvalidOperationException("Series event data is missing");

        return provider.ParseEventForEdit(new RawEvent
        {
            Reference = new EventReference
            {
                Calendar = calendarEvent.Reference.Calendar,
                Id = Guid.Parse(storedEvent.EventId),
                ExternalId = storedEvent.ExternalId,
            },
            RawData = rawData
        });
    }

    private static async Task<RecurringEventAction?> ShowRecurrenceActionDialogAsync(
        string title,
        string eventTitle,
        IReadOnlyList<RecurrenceActionOption> options)
    {
        var dialog = new RecurrenceActionDialog
        {
            DataContext = new RecurrenceActionDialogViewModel(title, eventTitle, options)
        };

        return await dialog.ShowDialog<RecurringEventAction?>(App.MainWindow
            ?? throw new InvalidOperationException("Main window not available"));
    }

    [RelayCommand]
    private void CreateNewEvent()
    {
        OpenEventEditor();
    }


    public abstract IReadOnlyList<CalendarEvent> GetEventsInCurrentRange();
    public abstract void Load();
}