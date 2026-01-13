using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using CredentialStore;
using perinma.Services;
using perinma.Storage;
using perinma.Views.Main;
using SQLitePCL;

namespace perinma;

public partial class App : Application
{
    private DatabaseService? _databaseService;
    private CredentialManagerService? _credentialManager;

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
            var syncService = new SyncService(storage, _credentialManager, googleCalendarService, calDavService);

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Avoid duplicate validations from both Avalonia and the CommunityToolkit.
                // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
                DisableAvaloniaDataAnnotationValidation();
                desktop.MainWindow = new MainWindow
                {
                    DataContext = new MainWindowViewModel(_databaseService, _credentialManager, syncService, calDavService),
                };
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