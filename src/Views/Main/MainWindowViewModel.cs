using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using perinma.Messaging;
using perinma.Services;
using perinma.Services.CalDAV;
using perinma.Services.Google;
using perinma.Storage;
using perinma.Views.Calendar;
using perinma.Views.CalendarList;
using perinma.Views.Settings;

namespace perinma.Views.Main;

public partial class MainWindowViewModel : ObservableRecipient,
    IRecipient<SyncStartedMessage>,
    IRecipient<SyncEndedMessage>,
    IRecipient<SyncAccountProgressMessage>,
    IRecipient<SyncCalendarProgressMessage>,
    IRecipient<SyncEventsProgressMessage>,
    IRecipient<SyncCompletedMessage>,
    IRecipient<SyncFailedMessage>
{
    private readonly DatabaseService _databaseService;
    private readonly CredentialManagerService _credentialManager;
    private readonly SyncService _syncService;
    private readonly GoogleCalendarService _googleCalendarService;
    private readonly GoogleOAuthService _googleOAuthService;
    private readonly ICalDavService _calDavService;
    private readonly ThemeService _themeService;
    private readonly SettingsService _settingsService;

    [ObservableProperty]
    private bool _isSyncing;

    [ObservableProperty]
    private string _syncStatusText = "Ready";

    [ObservableProperty]
    private double _syncProgress = 0.0;

    [ObservableProperty]
    private bool _syncProgressIsIndeterminate = true;

    public CalendarWeekViewModel CalendarWeekViewModel { get; }
    public CalendarListViewModel CalendarListViewModel { get; }

    public MainWindowViewModel(
        DatabaseService databaseService,
        CredentialManagerService credentialManager,
        SyncService syncService,
        ICalDavService calDavService)
    {
        _databaseService = databaseService;
        _credentialManager = credentialManager;
        _syncService = syncService;
        _calDavService = calDavService;
        _themeService = new ThemeService();

        var storage = new SqliteStorage(databaseService, credentialManager);
        var calendarSource = new DatabaseCalendarSource(storage);
        //var calendarSource = new DummyCalendarSource(DateTime.Now);
        _googleCalendarService = new GoogleCalendarService();
        _googleOAuthService = new GoogleOAuthService(_googleCalendarService);
        _settingsService = new SettingsService(storage);
        CalendarWeekViewModel = new CalendarWeekViewModel(calendarSource, storage, _settingsService, syncService.Providers, credentialManager);
        CalendarListViewModel = new CalendarListViewModel(storage, _googleCalendarService, credentialManager, CalendarWeekViewModel);

        Initialize();
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
        _settingsWindow.DataContext = new SettingsViewModel(_databaseService, _credentialManager, _googleOAuthService, _calDavService, _syncService, _settingsWindow);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
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

            var result = await _syncService.SyncAllAccountsAsync(cancellationToken);

            // Status updates are now handled by the Receive methods via messages
            if (result.Success)
            {
                Console.WriteLine($"Sync completed successfully. Synced {result.SyncedAccounts} accounts.");
                await CalendarListViewModel.LoadCalendarsAsync();
            }
            else
            {
                Console.WriteLine($"Sync completed with errors. Synced: {result.SyncedAccounts}, Failed: {result.FailedAccounts}");
                foreach (var error in result.Errors)
                {
                    Console.WriteLine($"  - {error}");
                }
            }
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
        IsSyncing = false;
        SyncProgress = 0.0;
        SyncStatusText = "Ready";
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
    #endregion

    #region Window Settings

    private void Initialize()
    {
        // Enable message registration
        IsActive = true;
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
