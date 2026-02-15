using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using perinma.Messaging;
using perinma.Services;
using perinma.Services.CalDAV;
using perinma.Services.CardDAV;
using perinma.Services.Google;
using perinma.Storage;
using perinma.Views.Calendar;
using perinma.Views.CalendarList;
using perinma.Views.Contacts;
using perinma.Views.Debug;
using perinma.Views.MessageBox;
using perinma.Views.Settings;

namespace perinma.Views.Main;

public partial class MainWindowViewModel : ObservableRecipient,
    IRecipient<SyncStartedMessage>,
    IRecipient<SyncEndedMessage>,
    IRecipient<SyncAccountProgressMessage>,
    IRecipient<SyncCalendarProgressMessage>,
    IRecipient<SyncEventsProgressMessage>,
    IRecipient<SyncCompletedMessage>,
    IRecipient<SyncFailedMessage>,
    IRecipient<ReAuthenticationRequiredMessage>,
    IRecipient<ContactSyncStartedMessage>,
    IRecipient<ContactSyncEndedMessage>,
    IRecipient<SyncAddressBookProgressMessage>,
    IRecipient<SyncContactsProgressMessage>,
    IRecipient<SyncContactProcessingProgressMessage>
{
    private readonly DatabaseService _databaseService;
    private readonly CredentialManagerService _credentialManager;
    private readonly SyncService _syncService;
    private readonly ContactSyncService _contactSyncService;
    private readonly GoogleCalendarService _googleCalendarService;
    private readonly GoogleOAuthService _googleOAuthService;
    private readonly ICalDavService _calDavService;
    private readonly ICardDavService _cardDavService;
    private readonly ThemeService _themeService;
    private readonly SettingsService _settingsService;
    private DebugWindow? _debugWindow;

    [ObservableProperty]
    private bool _isSyncing;

    [ObservableProperty]
    private string _syncStatusText = "Ready";

    [ObservableProperty]
    private double _syncProgress = 0.0;

    [ObservableProperty]
    private bool _syncProgressIsIndeterminate = true;

    // View switching
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsContactsViewActive))]
    private bool _isCalendarViewActive = true;

    public bool IsContactsViewActive => !IsCalendarViewActive;

    public CalendarWeekViewModel CalendarWeekViewModel { get; }
    public CalendarListViewModel CalendarListViewModel { get; }
    public ContactsViewModel ContactsViewModel { get; }

    public MainWindowViewModel(
        DatabaseService databaseService,
        CredentialManagerService credentialManager,
        SyncService syncService,
        ContactSyncService contactSyncService,
        ICalDavService calDavService,
        ICardDavService cardDavService)
    {
        _databaseService = databaseService;
        _credentialManager = credentialManager;
        _syncService = syncService;
        _contactSyncService = contactSyncService;
        _calDavService = calDavService;
        _cardDavService = cardDavService;
        _themeService = new ThemeService();

        var storage = new SqliteStorage(databaseService, credentialManager);
        var calendarSource = new DatabaseCalendarSource(storage, syncService.Providers);
        //var calendarSource = new DummyCalendarSource(DateTime.Now);
        _googleCalendarService = new GoogleCalendarService();
        _googleOAuthService = new GoogleOAuthService(_googleCalendarService);
        _settingsService = new SettingsService(storage);
        CalendarWeekViewModel = new CalendarWeekViewModel(calendarSource, storage, _settingsService, syncService.Providers);
        CalendarListViewModel = new CalendarListViewModel(storage, _googleCalendarService, credentialManager, CalendarWeekViewModel);
        ContactsViewModel = new ContactsViewModel(storage);

        Initialize();
    }

    [RelayCommand]
    private async Task ShowCalendarView()
    {
        IsCalendarViewActive = true;
        await CalendarWeekViewModel.InitializeAsync();
    }

    [RelayCommand]
    private void ShowContactsView()
    {
        IsCalendarViewActive = false;
    }

    #region Settings
    private SettingsWindow? _settingsWindow;

    [RelayCommand]
    private void ShowSettings()
    {
        if (_settingsWindow != null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow();
        _settingsWindow.DataContext = new SettingsViewModel(_databaseService, _credentialManager, _googleOAuthService, _calDavService, _cardDavService, _syncService, _settingsWindow);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }
    #endregion

    #region Debug
    [RelayCommand]
    private void ShowDebugWindow()
    {
        if (_debugWindow != null)
        {
            _debugWindow.Activate();
            return;
        }

        var storage = new SqliteStorage(_databaseService, _credentialManager);
        var reminderService = new ReminderService(storage, _syncService.Providers);

        _debugWindow = new DebugWindow();
        _debugWindow.DataContext = new DebugWindowViewModel(reminderService);
        _debugWindow.Closed += (_, _) => _debugWindow = null;
        _debugWindow.Show();
    }
    #endregion

    #region Theme
    [ObservableProperty]
    private bool _isLightTheme = true;

    [ObservableProperty]
    private bool _isDarkTheme;

    [RelayCommand]
    private void SetLightTheme()
    {
        _themeService.SetTheme(ThemeVariant.Light);
        IsLightTheme = true;
        IsDarkTheme = false;
    }

    [RelayCommand]
    private void SetDarkTheme()
    {
        _themeService.SetTheme(ThemeVariant.Dark);
        IsLightTheme = false;
        IsDarkTheme = true;
    }
    #endregion

    #region Sync
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task Sync(CancellationToken cancellationToken)
    {
        if (IsSyncing)
            return;

        try
        {
            Console.WriteLine("Starting sync...");

            // Sync calendars
            var calendarResult = await _syncService.SyncAllAccountsAsync(cancellationToken);

            // Status updates are now handled by the Receive methods via messages
            if (calendarResult.Success)
            {
                Console.WriteLine($"Calendar sync completed successfully. Synced {calendarResult.SyncedAccounts} accounts.");
                await CalendarListViewModel.LoadCalendarsAsync();
                CalendarWeekViewModel.Load();
            }
            else
            {
                Console.WriteLine($"Calendar sync completed with errors. Synced: {calendarResult.SyncedAccounts}, Failed: {calendarResult.FailedAccounts}");
                foreach (var error in calendarResult.Errors)
                {
                    Console.WriteLine($"  - {error}");
                }
                // Still refresh to show any events that were synced
                await CalendarListViewModel.LoadCalendarsAsync();
                CalendarWeekViewModel.Load();
            }

            // Sync contacts
            Console.WriteLine("Starting contact sync...");
            var contactResult = await _contactSyncService.SyncAllAccountsAsync(cancellationToken);

            if (contactResult.Success)
            {
                Console.WriteLine($"Contact sync completed successfully. Synced {contactResult.SyncedAccounts} accounts.");
                await ContactsViewModel.LoadAddressBooksAsync();
            }
            else
            {
                Console.WriteLine($"Contact sync completed with errors. Synced: {contactResult.SyncedAccounts}, Failed: {contactResult.FailedAccounts}");
                foreach (var error in contactResult.Errors)
                {
                    Console.WriteLine($"  - {error}");
                }
                // Still refresh the contact list to show any contacts that were synced
                await ContactsViewModel.LoadAddressBooksAsync();
            }

            SyncStatusText = "Ready";
        }
        catch (Exception ex)
        {
            SyncStatusText = $"Sync failed: {ex.Message}";
            Console.WriteLine($"Sync failed: {ex.Message}");
        }
    }

    public void Receive(SyncStartedMessage message)
    {
        IsSyncing = true;
        SyncProgress = 0.0;
        SyncProgressIsIndeterminate = true;
        SyncStatusText = "Starting sync...";
    }

    public void Receive(SyncEndedMessage message)
    {
        // Only reset syncing state - status text is managed by the Sync() method
        // to show completion/error messages before resetting to "Ready"
        IsSyncing = false;
        SyncProgress = 0.0;
    }

    public void Receive(SyncAccountProgressMessage message)
    {
        SyncStatusText = $"Syncing account {message.AccountIndex + 1} of {message.TotalAccounts}: {message.AccountName}";
        SyncProgress = message.ProgressPercentage;
        SyncProgressIsIndeterminate = false;
    }

    public void Receive(SyncCalendarProgressMessage message)
    {
        SyncStatusText = $"  Syncing calendar {message.CalendarIndex + 1} of {message.TotalCalendars}: {message.CalendarName}";
    }

    public void Receive(SyncEventsProgressMessage message)
    {
        SyncStatusText = $"  Syncing events for {message.CalendarName} ({message.EventCount} events)...";
    }

    public void Receive(SyncCompletedMessage message)
    {
        SyncStatusText = $"Sync completed successfully. Synced {message.SyncedAccounts} accounts.";
        Task.Run(async () =>
        {
            await Task.Delay(2000);
            IsSyncing = false;
            SyncProgress = 0.0;
            SyncStatusText = "Ready";
        });
    }

    public void Receive(SyncFailedMessage message)
    {
        SyncStatusText = $"Sync completed with {message.FailedAccounts} error(s).";
        Task.Run(async () =>
        {
            await Task.Delay(3000);
            IsSyncing = false;
            SyncProgress = 0.0;
            SyncStatusText = "Ready";
        });
    }

    public async void Receive(ReAuthenticationRequiredMessage message)
    {
        // Get the main window reference
        var mainWindow = Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (mainWindow == null)
        {
            Console.WriteLine("Unable to show re-authentication dialog - main window not found");
            return;
        }

        try
        {
            var result = await MessageBoxWindow.ShowAsync(
                mainWindow,
                "Re-authentication Required",
                $"Your {message.ProviderType} account requires re-authentication. Would you like to sign in again?",
                MessageBoxType.Warning,
                MessageBoxButtons.YesNo);

            if (result == MessageBoxResult.Yes)
            {
                if (message.ProviderType.Equals("Google", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        // Get the account details
                        var storage = new SqliteStorage(_databaseService, _credentialManager);
                        var account = await storage.GetAccountByIdAsync(message.AccountId);

                        if (account != null)
                        {
                            Console.WriteLine($"Starting re-authentication for account: {account.Name}");

                            // Perform authentication
                            var newCredentials = await _googleOAuthService.AuthenticateAsync();

                            // Update the credentials in the credential manager
                            _credentialManager.StoreGoogleCredentials(message.AccountId, newCredentials);

                            Console.WriteLine($"Re-authentication successful for account: {account.Name}");

                            await MessageBoxWindow.ShowAsync(
                                mainWindow,
                                "Authentication Successful",
                                $"Your {message.ProviderType} account has been successfully re-authenticated.",
                                MessageBoxType.Information,
                                MessageBoxButtons.Ok);
                        }
                        else
                        {
                            Console.WriteLine($"Account not found: {message.AccountId}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Re-authentication failed: {ex.Message}");
                        await MessageBoxWindow.ShowAsync(
                            mainWindow,
                            "Authentication Failed",
                            $"Failed to re-authenticate your {message.ProviderType} account: {ex.Message}",
                            MessageBoxType.Error,
                            MessageBoxButtons.Ok);
                    }
                }
                else
                {
                    await MessageBoxWindow.ShowAsync(
                        mainWindow,
                        "Not Implemented",
                        $"Re-authentication for {message.ProviderType} is not yet implemented.",
                        MessageBoxType.Information,
                        MessageBoxButtons.Ok);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in re-authentication flow: {ex.Message}");
        }
    }

    public void Receive(ContactSyncStartedMessage message)
    {
        SyncStatusText = "Syncing contacts...";
    }

    public void Receive(ContactSyncEndedMessage message)
    {
        // Contact sync ended - status will be updated by calendar sync completion
    }

    public void Receive(SyncAddressBookProgressMessage message)
    {
        SyncStatusText = $"  Syncing address book {message.AddressBookIndex + 1} of {message.TotalAddressBooks}: {message.AddressBookName}";
    }

    public void Receive(SyncContactsProgressMessage message)
    {
        SyncStatusText = $"  Syncing contacts for {message.AddressBookName} ({message.ContactCount} contacts)...";
    }

    public void Receive(SyncContactProcessingProgressMessage message)
    {
        SyncStatusText = $"  Syncing contact {message.ContactIndex + 1} of {message.TotalContacts} for {message.AddressBookName}...";
        SyncProgress = message.ProgressPercentage;
        SyncProgressIsIndeterminate = false;
    }
    #endregion

    #region Window Settings

    private async void Initialize()
    {
        // Enable message registration
        IsActive = true;

        // Load and restore last view state
        await LoadViewStateAsync();
    }

    private async Task LoadViewStateAsync()
    {
        try
        {
            var lastActiveView = await _settingsService.GetLastActiveViewAsync();
            if (lastActiveView.Equals("contacts", StringComparison.OrdinalIgnoreCase))
            {
                IsCalendarViewActive = false;
            }
            else
            {
                IsCalendarViewActive = true;
                await CalendarWeekViewModel.InitializeAsync();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load view state: {ex.Message}");
        }
    }

    public async Task SaveViewStateAsync()
    {
        try
        {
            // Save which view is active
            await _settingsService.SetLastActiveViewAsync(IsCalendarViewActive ? "calendar" : "contacts");

            // Save calendar view mode if in calendar view
            if (IsCalendarViewActive)
            {
                await _settingsService.SetLastCalendarViewModeAsync(CalendarWeekViewModel.ViewMode.ToString());
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save view state: {ex.Message}");
        }
    }

    public async Task SaveWindowSettingsAsync(int x, int y, int width, int height, int sidebarWidth)
    {
        await _settingsService.SetMainWindowXAsync(x);
        await _settingsService.SetMainWindowYAsync(y);
        await _settingsService.SetMainWindowWidthAsync(width);
        await _settingsService.SetMainWindowHeightAsync(height);
        await _settingsService.SetSidebarWidthAsync(sidebarWidth);
    }

    public async Task<(int x, int y, int width, int height, int sidebarWidth)> GetWindowSettingsAsync()
    {
        var x = await _settingsService.GetMainWindowXAsync();
        var y = await _settingsService.GetMainWindowYAsync();
        var width = await _settingsService.GetMainWindowWidthAsync();
        var height = await _settingsService.GetMainWindowHeightAsync();
        var sidebarWidth = await _settingsService.GetSidebarWidthAsync();
        return (x, y, width, height, sidebarWidth);
    }

    #endregion
}
