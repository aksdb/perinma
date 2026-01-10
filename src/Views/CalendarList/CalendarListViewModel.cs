using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using perinma.Storage;
using perinma.Views.Calendar;

namespace perinma.Views.CalendarList;

public partial class CalendarListViewModel : ViewModelBase
{
    private readonly SqliteStorage _storage;

    public ObservableCollection<AccountGroupViewModel> AccountGroups { get; } = new();

    public CalendarListViewModel(SqliteStorage storage)
    {
        _storage = storage;
        _ = LoadCalendarsAsync();
    }

    public async Task LoadCalendarsAsync()
    {
        AccountGroups.Clear();

        try
        {
            var accounts = await _storage.GetAllAccountsAsync();

            foreach (var account in accounts)
            {
                var accountGroup = new AccountGroupViewModel
                {
                    AccountId = Guid.Parse(account.AccountId),
                    AccountName = account.Name
                };

                var calendars = await _storage.GetCalendarsByAccountAsync(account.AccountId);

                foreach (var calendar in calendars)
                {
                    var calendarViewModel = new CalendarViewModel
                    {
                        Id = Guid.Parse(calendar.CalendarId),
                        Name = calendar.Name,
                        Color = calendar.Color,
                        Enabled = calendar.Enabled != 0
                    };

                    calendarViewModel.EnabledChanged += OnCalendarEnabledChanged;
                    accountGroup.Calendars.Add(calendarViewModel);
                }

                if (accountGroup.Calendars.Count > 0)
                {
                    AccountGroups.Add(accountGroup);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading calendars: {ex.Message}");
        }
    }

    private async void OnCalendarEnabledChanged(object? sender, bool enabled)
    {
        if (sender is not CalendarViewModel calendar)
            return;

        try
        {
            var success = await _storage.UpdateCalendarEnabledAsync(
                calendar.Id.ToString(),
                enabled
            );

            if (!success)
            {
                Console.WriteLine($"Failed to update calendar enabled state: {calendar.Id}");
                calendar.Enabled = !enabled;
                return;
            }

            CalendarWeekViewModel.Instance.Load();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error updating calendar enabled state: {ex.Message}");
            calendar.Enabled = !enabled;
        }
    }
}
