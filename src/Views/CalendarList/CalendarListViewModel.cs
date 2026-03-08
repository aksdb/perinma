using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using perinma.Messaging;
using perinma.Models;
using perinma.Services;
using perinma.Services.Google;
using perinma.Storage;
using perinma.Views.Calendar;

namespace perinma.Views.CalendarList;

public partial class CalendarListViewModel : ViewModelBase, IRecipient<AccountsChangedMessage>
{
    private readonly SqliteStorage _storage;
    private readonly IGoogleCalendarService _googleCalendarService;
    private readonly CredentialManagerService _credentialManager;
    private readonly CalendarWeekViewModel _calendarWeekViewModel;
    private bool _isLoadingAccounts;

    public ObservableCollection<AccountGroupViewModel> AccountGroups { get; } = new();

    public CalendarWeekViewModel CalendarWeekViewModel => _calendarWeekViewModel;

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
        WeakReferenceMessenger.Default.Register<AccountsChangedMessage>(this);
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
            var accounts = _storage.GetCachedAccounts().ToList();

            foreach (var account in accounts)
            {
                var accountGroup = new AccountGroupViewModel
                {
                    AccountId = account.Id,
                    AccountName = account.Name
                };

                var calendars = _storage.GetCachedCalendars(account).ToList();
                    
                foreach (var calendar in calendars)
                {
                    var calendarViewModel = new CalendarViewModel(calendar)
                    {
                        Url = calendar.ExternalId,
                        IsCalDav = account.Type == AccountType.CalDav
                    };

                    // Set services for ACL management
                    calendarViewModel.SetServices(_storage, _credentialManager);

                    // Load ACL data for CalDAV calendars
                    if (account.Type == AccountType.CalDav)
                    {
                        try
                        {
                            // TODO can we do this in a better place? This feels like something for the provider.
                            calendarViewModel.AclXml = await _storage.GetCalendarDataAsync(calendar.Id.ToString(), "rawACL");
                            calendarViewModel.CurrentUserPrivilegeSetXml = await _storage.GetCalendarDataAsync(calendar.Id.ToString(), "currentUserPrivilegeSet");
                            calendarViewModel.Owner = await _storage.GetCalendarDataAsync(calendar.Id.ToString(), "owner");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error loading ACL data for calendar '{calendar.Name}': {ex.Message}");
                        }
                    }

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

    public void Receive(AccountsChangedMessage message)
    {
        Dispatcher.UIThread.Post(() => _ = LoadCalendarsAsync());
    }
}
