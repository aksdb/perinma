using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using CredentialStore;
using perinma.Models;
using perinma.Services;
using perinma.Services.CalDAV;
using perinma.Services.CardDAV;
using perinma.Services.Google;
using perinma.Storage;
using perinma.Views.Main;
using SQLitePCL;

namespace perinma;

public partial class App : Application
{
    private DatabaseService? _databaseService;
    private CredentialManagerService? _credentialManager;
    private ReminderSchedulerService? _reminderSchedulerService;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Batteries.Init();
        try
        {
            _databaseService = new DatabaseService();
            _credentialManager = new CredentialManagerService(PlatformCredentialStore.Create("perinma"));

            // Initialize storage and services
            var storage = new SqliteStorage(_databaseService, _credentialManager);
            var googleCalendarService = new GoogleCalendarService();
            var googleOAuthService = new GoogleOAuthService(googleCalendarService);
            var calDavService = new CalDavService();

            // Create calendar providers
            var googleCalendarProvider = new GoogleCalendarProvider(googleCalendarService, _credentialManager);
            var calDavCalendarProvider = new CalDavCalendarProvider(calDavService, _credentialManager);

            // Create calendar providers dictionary for ReminderService
            var calendarProviders = new Dictionary<AccountType, ICalendarProvider>
            {
                [AccountType.Google] = googleCalendarProvider,
                [AccountType.CalDav] = calDavCalendarProvider
            };

            var reminderService = new ReminderService(storage, calendarProviders);
            var syncService = new SyncService(storage, _credentialManager, calendarProviders, reminderService);

            // Create contact providers
            var googlePeopleService = new GooglePeopleService();
            var googleContactProvider = new GoogleContactProvider(googlePeopleService, _credentialManager);
            var cardDavService = new CardDavService();
            var cardDavContactProvider = new CardDavContactProvider(cardDavService, _credentialManager);

            // Create contact providers dictionary
            var contactProviders = new Dictionary<AccountType, IContactProvider>
            {
                [AccountType.Google] = googleContactProvider,
                [AccountType.CardDav] = cardDavContactProvider
            };

            var contactSyncService = new ContactSyncService(storage, contactProviders);

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Avoid duplicate validations from both Avalonia and the CommunityToolkit.
                // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
                DisableAvaloniaDataAnnotationValidation();
                desktop.MainWindow = new MainWindow
                {
                    DataContext = new MainWindowViewModel(_databaseService, _credentialManager, syncService, contactSyncService, calDavService, cardDavService),
                };

                // Initialize and start the reminder scheduler
                _reminderSchedulerService = new ReminderSchedulerService(reminderService, desktop.MainWindow);
                _reminderSchedulerService.Start();

                // Stop the scheduler when the app exits
                desktop.Exit += (_, _) => _reminderSchedulerService?.Stop();
            }
        }
        catch (Exception ex)
        {
            // For now, we just log to console or rethrow.
            Console.WriteLine($"Failed to initialize services: {ex}");
            throw;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}