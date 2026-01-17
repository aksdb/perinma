using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using perinma.Services;
using perinma.Storage;
using perinma.Views.Calendar;
using perinma.Views.CalendarList;
using perinma.Views.Settings;

namespace perinma.Views.Main;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly DatabaseService _databaseService;
    private readonly CredentialManagerService _credentialManager;
    private readonly SyncService _syncService;
    private readonly GoogleCalendarService _googleCalendarService;
    private readonly GoogleOAuthService _googleOAuthService;
    private readonly ICalDavService _calDavService;
    private readonly ThemeService _themeService;

    [ObservableProperty]
    private bool _isSyncing;

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
        CalendarWeekViewModel = new CalendarWeekViewModel(calendarSource, storage);
        CalendarListViewModel = new CalendarListViewModel(storage, _googleCalendarService, credentialManager, CalendarWeekViewModel);
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

        IsSyncing = true;

        try
        {
            Console.WriteLine("Starting sync...");
            var result = await _syncService.SyncAllAccountsAsync(cancellationToken);

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
            Console.WriteLine($"Sync failed: {ex.Message}");
        }
        finally
        {
            IsSyncing = false;
        }
    }
    #endregion
}
