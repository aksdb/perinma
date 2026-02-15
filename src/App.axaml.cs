using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using perinma.DependencyInjection;
using perinma.Services;
using perinma.Views.Main;
using SQLitePCL;

namespace perinma;

public partial class App : Application
{
    public static IServiceProvider? Services { get; private set; }
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
            var services = new ServiceCollection();
            services.AddPerinmaServices();

            Services = services.BuildServiceProvider();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                DisableAvaloniaDataAnnotationValidation();
                desktop.MainWindow = new MainWindow
                {
                    DataContext = Services.GetRequiredService<MainWindowViewModel>(),
                };

                var reminderService = Services.GetRequiredService<ReminderService>();
                _reminderSchedulerService = new ReminderSchedulerService(reminderService, desktop.MainWindow);
                _reminderSchedulerService.Start();

                desktop.Exit += (_, _) => _reminderSchedulerService?.Stop();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to initialize services: {ex}");
            throw;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}