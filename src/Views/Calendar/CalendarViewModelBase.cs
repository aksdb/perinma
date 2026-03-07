using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using perinma.Messaging;
using perinma.Models;
using perinma.Services;
using perinma.Storage;
using perinma.Views.MessageBox;

namespace perinma.Views.Calendar;

public abstract partial class CalendarViewModelBase : ViewModelBase
{
    protected readonly ICalendarSource _calendarSource;
    protected readonly SqliteStorage _storage;

    public SettingsService? SettingsService { get; }

    protected CalendarViewModelBase(ICalendarSource calendarSource, SettingsService? settingsService = null)
    {
        _calendarSource = calendarSource;
        SettingsService = settingsService;

        var storage = App.Services?.GetRequiredService<SqliteStorage>();
        if (storage == null)
        {
            throw new InvalidOperationException("SqliteStorage not available");
        }

        _storage = storage;
    }

    [RelayCommand]
    private void EditEvent(CalendarEvent? eventToEdit)
    {
        if (eventToEdit == null) return;
        OpenEventEditor(eventToEdit);
    }

    [RelayCommand]
    private async Task DeleteEventAsync(CalendarEvent? eventToDelete)
    {
        if (eventToDelete == null) return;

        var eventTitle = eventToDelete.Title ?? "[no title]";

        var result = await MessageBoxWindow.ShowAsync(
            null,
            "Delete Event",
            $"Are you sure you want to delete \"{eventTitle}\"?",
            MessageBoxType.Warning,
            MessageBoxButtons.YesNo);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        var accountId = eventToDelete.Reference.Calendar.Account.Id.ToString();
        var calendarId = eventToDelete.Reference.Calendar.ExternalId ?? string.Empty;
        var eventId = eventToDelete.Reference.ExternalId ?? string.Empty;

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

            await provider.DeleteEventAsync(accountId, calendarId, eventId);
            await _storage.DeleteEventByExternalIdAsync(eventToDelete.Reference.Calendar.Id.ToString(), eventId);
            WeakReferenceMessenger.Default.Send(new EventsChangedMessage());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to delete event: {ex}");
            throw;
        }
    }

    public void OpenEventEditor(CalendarEvent? existingEvent = null, 
        DateTime? initialStartTime = null, 
        DateTime? initialEndTime = null,
        bool isFullDay = false)
    {
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
                existingEvent: existingEvent,
                calendar: existingEvent?.Reference.Calendar,
                onCompleted: onCompleted,
                initialStartTime: initialStartTime,
                initialEndTime: initialEndTime,
                isFullDay: isFullDay
            )
        };
        editor.Show();
    }
    
    [RelayCommand]
    private void CreateNewEvent()
    {
        OpenEventEditor();
    }

    public abstract void Load();
}