using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
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
    private readonly SqliteStorage _storage;
    private readonly ICalendarSource _calendarSource;
    private DebugWindow? _debugWindow;
    private System.Threading.Timer? _autoSyncTimer;

    [ObservableProperty]
    public partial bool IsSyncing { get; set; }

    [ObservableProperty]
    public partial string SyncStatusText { get; set; } = "Ready";

    [ObservableProperty]
    public partial double SyncProgress { get; set; } = 0.0;

    [ObservableProperty]
    public partial bool SyncProgressIsIndeterminate { get; set; } = true;

    // View switching
    public enum MainViewMode
    {
        Calendar,
        Contacts
    }

    private bool _suppressSelectedMainViewIndexChanged;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsContactsViewActive))]
    public partial bool IsCalendarViewActive { get; set; } = true;

    [ObservableProperty]
    public partial int SelectedMainViewIndex { get; set; } = (int)MainViewMode.Calendar;

    public bool IsContactsViewActive => !IsCalendarViewActive;

    partial void OnIsCalendarViewActiveChanged(bool value)
    {
        var targetIndex = value ? (int)MainViewMode.Calendar : (int)MainViewMode.Contacts;
        if (SelectedMainViewIndex == targetIndex)
        {
            return;
        }

        _suppressSelectedMainViewIndexChanged = true;
        try
        {
            SelectedMainViewIndex = targetIndex;
        }
        finally
        {
            _suppressSelectedMainViewIndexChanged = false;
        }
    }

    partial void OnSelectedMainViewIndexChanged(int value)
    {
        if (_suppressSelectedMainViewIndexChanged)
        {
            return;
        }

        switch ((MainViewMode)value)
        {
            case MainViewMode.Calendar:
                if (!IsCalendarViewActive)
                {
                    ShowCalendarView();
                }
                break;
            case MainViewMode.Contacts:
                if (IsCalendarViewActive)
                {
                    ShowContactsView();
                }
                break;
        }
    }
    public enum CalendarView
    {
        Month,
        Week,
        Agenda
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMonthView))]
    [NotifyPropertyChangedFor(nameof(IsWeekView))]
    [NotifyPropertyChangedFor(nameof(IsAgendaView))]
    public partial CalendarView CalendarViewMode { get; set; } = CalendarView.Week;

    public bool IsMonthView => CalendarViewMode == CalendarView.Month;
    public bool IsWeekView => CalendarViewMode == CalendarView.Week;
    public bool IsAgendaView => CalendarViewMode == CalendarView.Agenda;

    public CalendarMonthViewModel CalendarMonthViewModel { get; }
    public CalendarWeekViewModel CalendarWeekViewModel { get; }
    public CalendarAgendaViewModel CalendarAgendaViewModel { get; }
    public CalendarNavigationBarViewModel CalendarNavigationBarViewModel { get; }
    public CalendarListViewModel CalendarListViewModel { get; }
    public ContactsViewModel ContactsViewModel { get; }

    [ObservableProperty]
    public partial CalendarViewModelBase ActiveCalendarViewModel { get; set; } = null!;

    private CalendarViewModelBase? _dateRangeSubscriptionTarget;

    partial void OnActiveCalendarViewModelChanged(CalendarViewModelBase value)
    {
        if (_dateRangeSubscriptionTarget != null)
        {
            _dateRangeSubscriptionTarget.PropertyChanged -= OnActiveVmPropertyChanged;
        }

        _dateRangeSubscriptionTarget = value;
        value.PropertyChanged += OnActiveVmPropertyChanged;
    }

    private void OnActiveVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(CalendarViewModelBase.DateRangeDisplay))
        {
            CalendarNavigationBarViewModel.DateRangeDisplay = ActiveCalendarViewModel.DateRangeDisplay;
        }
    }

    public MainWindowViewModel(
        DatabaseService databaseService,
        CredentialManagerService credentialManager,
        SyncService syncService,
        ContactSyncService contactSyncService,
        ICalDavService calDavService,
        ICardDavService cardDavService,
        ThemeService themeService,
        SettingsService settingsService,
        SqliteStorage storage,
        GoogleCalendarService googleCalendarService,
        GoogleOAuthService googleOAuthService,
        ICalendarSource calendarSource)
    {
        _databaseService = databaseService;
        _credentialManager = credentialManager;
        _syncService = syncService;
        _contactSyncService = contactSyncService;
        _calDavService = calDavService;
        _cardDavService = cardDavService;
        _themeService = themeService;
        _settingsService = settingsService;
        _storage = storage;
        _calendarSource = calendarSource;
        _googleCalendarService = googleCalendarService;
        _googleOAuthService = googleOAuthService;

        CalendarMonthViewModel = new CalendarMonthViewModel(calendarSource, _settingsService);
        CalendarWeekViewModel = new CalendarWeekViewModel(calendarSource, _settingsService);
        CalendarAgendaViewModel = new CalendarAgendaViewModel(calendarSource, _settingsService);
        CalendarNavigationBarViewModel = new CalendarNavigationBarViewModel();
        CalendarListViewModel = new CalendarListViewModel(_storage, calendarSource, _googleCalendarService, _credentialManager);
        ContactsViewModel = new ContactsViewModel(_storage, _contactSyncService);

        CalendarWeekViewModel.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(CalendarWeekViewModel.DayColumns))
            {
                SetupNavigationBar();
            }
        };

        WeakReferenceMessenger.Default.Register<WorkingDaysChangedMessage>(this, (r, m) =>
        {
            if (ResolveNavigationViewMode(CalendarViewMode, CalendarWeekViewModel.DayColumns, CalendarWeekViewModel.WorkWeekDayCount)
                == CalendarNavigationBarViewModel.CalendarNavigationViewMode.WorkWeek)
            {
                CalendarWeekViewModel.DayColumns = CalendarWeekViewModel.WorkWeekDayCount;
                LoadCurrentCalendarView();
            }
        });
    }

    public void AfterLoad()
    {
        SetupNavigationBar();
        Initialize();
    }

    [RelayCommand]
    private void ShowCalendarView()
    {
        IsCalendarViewActive = true;
        LoadCurrentCalendarView();
    }

    partial void OnCalendarViewModeChanged(CalendarView value)
    {
        SetupNavigationBar();
    }

    private CalendarViewModelBase ResolveActiveViewModel() => CalendarViewMode switch
    {
        CalendarView.Month => CalendarMonthViewModel,
        CalendarView.Week => CalendarWeekViewModel,
        CalendarView.Agenda => CalendarAgendaViewModel,
        _ => CalendarWeekViewModel
    };

    private static CalendarNavigationBarViewModel.CalendarNavigationViewMode ResolveNavigationViewMode(
        CalendarView calendarViewMode,
        int dayColumns,
        int workWeekDayCount)
    {
        return calendarViewMode switch
        {
            CalendarView.Month => CalendarNavigationBarViewModel.CalendarNavigationViewMode.Month,
            CalendarView.Agenda => CalendarNavigationBarViewModel.CalendarNavigationViewMode.Agenda,
            CalendarView.Week when dayColumns == 1 => CalendarNavigationBarViewModel.CalendarNavigationViewMode.Day,
            CalendarView.Week when dayColumns == workWeekDayCount => CalendarNavigationBarViewModel.CalendarNavigationViewMode.WorkWeek,
            CalendarView.Week => CalendarNavigationBarViewModel.CalendarNavigationViewMode.Week,
            _ => CalendarNavigationBarViewModel.CalendarNavigationViewMode.Week
        };
    }

    private void SetupNavigationBar()
    {
        ActiveCalendarViewModel = ResolveActiveViewModel();
        CalendarListViewModel.ActiveCalendarViewModel = ActiveCalendarViewModel;

        CalendarNavigationBarViewModel.SetSelectedViewMode(
            ResolveNavigationViewMode(
                CalendarViewMode,
                CalendarWeekViewModel.DayColumns,
                CalendarWeekViewModel.WorkWeekDayCount));

        CalendarNavigationBarViewModel.ShowMonthViewCommand = ShowMonthViewCommand;
        CalendarNavigationBarViewModel.ShowWeekViewCommand = ShowWeekViewCommand;
        CalendarNavigationBarViewModel.ShowFiveDaysViewCommand = ShowFiveDaysViewCommand;
        CalendarNavigationBarViewModel.ShowDayViewCommand = ShowDayViewCommand;
        CalendarNavigationBarViewModel.ShowAgendaViewCommand = ShowAgendaViewCommand;

        CalendarNavigationBarViewModel.PreviousCommand = ActiveCalendarViewModel.PreviousCommand;
        CalendarNavigationBarViewModel.NextCommand = ActiveCalendarViewModel.NextCommand;
        CalendarNavigationBarViewModel.TodayCommand = ActiveCalendarViewModel.TodayCommand;
        CalendarNavigationBarViewModel.CreateNewEventCommand = ActiveCalendarViewModel.CreateNewEventCommand;
        CalendarNavigationBarViewModel.DateRangeDisplay = ActiveCalendarViewModel.DateRangeDisplay;
    }

    private void LoadCurrentCalendarView()
    {
        switch (CalendarViewMode)
        {
            case CalendarView.Month:
                CalendarMonthViewModel.Load();
                break;
            case CalendarView.Week:
                CalendarWeekViewModel.Load();
                break;
            case CalendarView.Agenda:
                CalendarAgendaViewModel.Load();
                break;
        }
    }

    [RelayCommand]
    private void ShowMonthView()
    {
        CalendarViewMode = CalendarView.Month;
        LoadCurrentCalendarView();
    }

    [RelayCommand]
    private void ShowWeekView()
    {
        CalendarViewMode = CalendarView.Week;
        CalendarWeekViewModel.DayColumns = 7;
        LoadCurrentCalendarView();
    }

    [RelayCommand]
    private void ShowFiveDaysView()
    {
        CalendarViewMode = CalendarView.Week;
        CalendarWeekViewModel.DayColumns = CalendarWeekViewModel.WorkWeekDayCount;
        LoadCurrentCalendarView();
    }

    [RelayCommand]
    private void ShowDayView()
    {
        CalendarViewMode = CalendarView.Week;
        CalendarWeekViewModel.DayColumns = 1;
        LoadCurrentCalendarView();
    }

    [RelayCommand]
    private void ShowAgendaView()
    {
        CalendarViewMode = CalendarView.Agenda;
        LoadCurrentCalendarView();
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
        _settingsWindow.DataContext = new SettingsViewModel(_databaseService, _credentialManager, _googleOAuthService, _calDavService, _cardDavService, _syncService, _settingsWindow, _storage);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }
    #endregion

    #region About
    private AboutDialogWindow? _aboutWindow;

    [RelayCommand]
    private void ShowAbout()
    {
        if (_aboutWindow != null)
        {
            _aboutWindow.Activate();
            return;
        }

        var viewModel = new AboutDialogViewModel();
        _aboutWindow = new AboutDialogWindow();
        _aboutWindow.SetViewModel(viewModel);
        _aboutWindow.Closed += (_, _) => _aboutWindow = null;
        _aboutWindow.Show();
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

        var reminderService = new ReminderService(_storage, _calendarSource, _syncService.Providers);

        _debugWindow = new DebugWindow();
        _debugWindow.DataContext = new DebugWindowViewModel(reminderService);
        _debugWindow.Closed += (_, _) => _debugWindow = null;
        _debugWindow.Show();
    }
    #endregion

    #region Theme
    [ObservableProperty]
    public partial bool IsLightTheme { get; set; } = true;

    [ObservableProperty]
    public partial bool IsDarkTheme { get; set; }

    [RelayCommand]
    private void SetLightTheme()
    {
        _themeService.SetLightTheme();
        UpdateThemeFlags();
    }

    [RelayCommand]
    private void SetDarkTheme()
    {
        _themeService.SetDarkTheme();
        UpdateThemeFlags();
    }

    public Task SaveThemeAsync() => _themeService.SaveThemeAsync();

    private void UpdateThemeFlags()
    {
        IsLightTheme = _themeService.IsLightTheme;
        IsDarkTheme = _themeService.IsDarkTheme;
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
            Console.WriteLine($"Sync failed: {ex}");
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
            if (message.ProviderType.Equals("Google", StringComparison.OrdinalIgnoreCase))
            {
                // Get the account details
                var account = await _storage.GetAccountByIdAsync(message.AccountId);

                if (account != null)
                {
                    Console.WriteLine($"Starting re-authentication for account: {account.Name}");

                    var viewModel = new ReauthenticationDialogViewModel(
                        message.AccountId,
                        account.Name,
                        _credentialManager,
                        _googleOAuthService,
                        _googleCalendarService);

                    var dialog = new ReauthenticationDialogWindow();
                    dialog.SetViewModel(viewModel);
                    await dialog.ShowDialog(mainWindow);
                }
                else
                {
                    Console.WriteLine($"Account not found: {message.AccountId}");
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

        // Load and restore theme
        await _themeService.LoadThemeAsync();
        UpdateThemeFlags();

        // Load and restore last view state
        await LoadViewStateAsync();

        // Start auto sync timer
        StartAutoSyncTimer();
    }

    private async void StartAutoSyncTimer()
    {
        try
        {
            var intervalMinutes = await _settingsService.GetAutoSyncIntervalAsync();
            var intervalMs = intervalMinutes * 60 * 1000;

            _autoSyncTimer = new System.Threading.Timer(
                _ => 
                {
                    if (!IsSyncing)
                    {
                        Dispatcher.UIThread.InvokeAsync(async () => 
                        {
                            try
                            {
                                Console.WriteLine("Auto sync triggered");
                                await Sync(CancellationToken.None);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Auto sync failed: {ex.Message}");
                            }
                        });
                    }
                },
                null,
                intervalMs,
                intervalMs);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to start auto sync timer: {ex.Message}");
        }
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
                var lastCalendarView = await _settingsService.GetLastCalendarViewModeAsync();
                if (Enum.TryParse<CalendarView>(lastCalendarView, out var viewMode))
                {
                    CalendarViewMode = viewMode;
                    
                    // Restore DayColumns when in Week view
                    if (CalendarViewMode == CalendarView.Week)
                    {
                        var lastDayColumns = await _settingsService.GetLastCalendarDayColumnsAsync();
                        CalendarWeekViewModel.DayColumns = lastDayColumns;
                    }
                }
                LoadCurrentCalendarView();
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
                await _settingsService.SetLastCalendarViewModeAsync(CalendarViewMode.ToString());
                
                // Save DayColumns when in Week view
                if (CalendarViewMode == CalendarView.Week)
                {
                    await _settingsService.SetLastCalendarDayColumnsAsync(CalendarWeekViewModel.DayColumns);
                }
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

    public void Cleanup()
    {
        _autoSyncTimer?.Dispose();
    }

    #endregion
}
