using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using perinma.Models;
using perinma.Services;
using perinma.Services.Google;
using perinma.Storage;
using perinma.Views.Calendar;

namespace perinma.Views.CalendarList;

public partial class CalendarListViewModel : ViewModelBase
{
    private readonly SqliteStorage _storage;
    private readonly IGoogleCalendarService _googleCalendarService;
    private readonly CredentialManagerService _credentialManager;
    private readonly CalendarWeekViewModel _calendarWeekViewModel;
    private bool _isLoadingAccounts;

    public ObservableCollection<AccountGroupViewModel> AccountGroups { get; } = new();

    public CalendarListViewModel(
        SqliteStorage storage,
        IGoogleCalendarService googleCalendarService,
        CredentialManagerService credentialManager,
        CalendarWeekViewModel calendarWeekViewModel)
    {
        _storage = storage;
        _googleCalendarService = googleCalendarService;
        _credentialManager = credentialManager;
        _calendarWeekViewModel = calendarWeekViewModel;
        AccountGroups.CollectionChanged += OnAccountGroupsCollectionChanged;
        _ = LoadCalendarsAsync();
    }

    private async void OnAccountGroupsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Skip if we're loading accounts (not a user reorder)
        if (_isLoadingAccounts)
            return;

        // Only handle Move actions (drag & drop reorder)
        if (e.Action == NotifyCollectionChangedAction.Move)
        {
            await SaveAccountSortOrderAsync();
        }
    }

    private async Task SaveAccountSortOrderAsync()
    {
        try
        {
            var sortOrders = AccountGroups
                .Select((group, index) => (group.AccountId.ToString(), index))
                .ToList();

            await _storage.UpdateAccountSortOrdersAsync(sortOrders);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving account sort order: {ex.Message}");
        }
    }

    public async Task LoadCalendarsAsync()
    {
        _isLoadingAccounts = true;
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
        finally
        {
            _isLoadingAccounts = false;
        }
    }

    private async void OnCalendarEnabledChanged(object? sender, bool enabled)
    {
        if (sender is not CalendarViewModel calendar)
            return;

        try
        {
            // Get the full calendar record from database
            var calendarDbo = await _storage.GetCalendarByIdAsync(calendar.Id.ToString());
            if (calendarDbo == null)
            {
                Console.WriteLine($"Calendar not found: {calendar.Id}");
                calendar.Enabled = !enabled;
                return;
            }

            // Get the account to check if it's a Google account
            var account = await _storage.GetAccountByIdAsync(calendarDbo.AccountId);
            if (account == null)
            {
                Console.WriteLine($"Account not found: {calendarDbo.AccountId}");
                calendar.Enabled = !enabled;
                return;
            }

            // Only sync with Google for Google accounts
            if (account.AccountTypeEnum == AccountType.Google && !string.IsNullOrEmpty(calendarDbo.ExternalId) && (calendarDbo.Enabled == 1) != enabled)
            {
                try
                {
                    // Get credentials for the account
                    var credentials = _credentialManager.GetGoogleCredentials(account.AccountId);
                    if (credentials == null)
                    {
                        Console.WriteLine($"No credentials found for account: {account.AccountId}");
                        calendar.Enabled = !enabled;
                        return;
                    }

                    // Create Google Calendar service
                    var service = await _googleCalendarService.CreateServiceAsync(credentials);

                    // Update the selected state in Google Calendar
                    await _googleCalendarService.UpdateCalendarSelectedAsync(
                        service,
                        calendarDbo.ExternalId,
                        enabled
                    );

                    Console.WriteLine($"Successfully synced calendar '{calendar.Name}' enabled state to Google Calendar");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to sync calendar enabled state to Google: {ex.Message}");
                    // Revert the UI change since Google update failed
                    calendar.Enabled = !enabled;
                    return;
                }
            }

            // Update local database
            var success = await _storage.UpdateCalendarEnabledAsync(
                calendar.Id.ToString(),
                enabled
            );

            if (!success)
            {
                Console.WriteLine($"Failed to update calendar enabled state in database: {calendar.Id}");
                calendar.Enabled = !enabled;
                return;
            }

            // Refresh the calendar view to show/hide events
            _calendarWeekViewModel.Load();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error updating calendar enabled state: {ex.Message}");
            calendar.Enabled = !enabled;
        }
    }
}
