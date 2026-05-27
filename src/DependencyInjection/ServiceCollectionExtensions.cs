using System.Collections.Generic;
using CredentialStore;
using Microsoft.Extensions.DependencyInjection;
using perinma.Models;
using perinma.Services;
using perinma.Services.CalDAV;
using perinma.Services.CardDAV;
using perinma.Services.Google;
using perinma.Services.Jmap;
using perinma.Storage;

namespace perinma.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPerinmaServices(this IServiceCollection services)
    {
        // Core services
        services.AddSingleton<DatabaseService>();
        services.AddSingleton<CredentialManagerService>(sp =>
            new CredentialManagerService(PlatformCredentialStore.Create("perinma")));
        services.AddSingleton<SqliteStorage>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<DebugFeaturesService>();


        // Google services
        services.AddSingleton<IGoogleCalendarService, GoogleCalendarService>();
        services.AddSingleton<GoogleCalendarService>();
        services.AddSingleton<GoogleOAuthService>();
        services.AddSingleton<IGooglePeopleService, GooglePeopleService>();
        services.AddSingleton<GooglePeopleService>();
        services.AddSingleton<GoogleCalendarProvider>();
        services.AddSingleton<GoogleContactProvider>();
        services.AddSingleton<GoogleMailService>();
        services.AddSingleton<GoogleMailProvider>();

        // CalDAV services
        services.AddSingleton<ICalDavService, CalDavService>();
        services.AddSingleton<CalDavService>();
        services.AddSingleton<CalDavCalendarProvider>();

        // CardDAV services
        services.AddSingleton<ICardDavService, CardDavService>();
        services.AddSingleton<CardDavService>();
        services.AddSingleton<CardDavContactProvider>();

        // JMAP mail services
        services.AddSingleton<JmapMailService>();
        services.AddSingleton<JmapMailProvider>();

        services.AddSingleton<IReadOnlyDictionary<AccountType, ICalendarProvider>>(sp =>
            new Dictionary<AccountType, ICalendarProvider>
            {
                [AccountType.Google] = sp.GetRequiredService<GoogleCalendarProvider>(),
                [AccountType.CalDav] = sp.GetRequiredService<CalDavCalendarProvider>()
            });

        services.AddSingleton<IReadOnlyDictionary<AccountType, IMailProvider>>(sp =>
            new Dictionary<AccountType, IMailProvider>
            {
                [AccountType.Google] = sp.GetRequiredService<GoogleMailProvider>(),
                [AccountType.Jmap] = sp.GetRequiredService<JmapMailProvider>()
            });

        // ReminderService - requires calendar providers
        services.AddSingleton<ReminderService>(sp =>
            new ReminderService(
                sp.GetRequiredService<SqliteStorage>(),
                sp.GetRequiredService<ICalendarSource>(),
                sp.GetRequiredService<IReadOnlyDictionary<AccountType, ICalendarProvider>>()));

        // SyncService - requires calendar providers
        services.AddSingleton<SyncService>(sp =>
            new SyncService(
                sp.GetRequiredService<SqliteStorage>(),
                sp.GetRequiredService<CredentialManagerService>(),
                sp.GetRequiredService<IReadOnlyDictionary<AccountType, ICalendarProvider>>(),
                sp.GetRequiredService<ReminderService>()));

        // MailSyncService - requires mail providers
        services.AddSingleton<MailSyncService>(sp =>
            new MailSyncService(
                sp.GetRequiredService<SqliteStorage>(),
                sp.GetRequiredService<IReadOnlyDictionary<AccountType, IMailProvider>>()));

        // ContactSyncService - requires contact providers
        services.AddSingleton<ContactSyncService>(sp =>
        {
            var storage = sp.GetRequiredService<SqliteStorage>();
            var providers = new Dictionary<AccountType, IContactProvider>
            {
                [AccountType.Google] = sp.GetRequiredService<GoogleContactProvider>(),
                [AccountType.CardDav] = sp.GetRequiredService<CardDavContactProvider>()
            };
            return new ContactSyncService(storage, providers);
        });

        // ICalendarSource — combines SqliteStorage + provider parse/recurrence logic
        services.AddSingleton<ICalendarSource>(sp => new DatabaseCalendarSource(
            sp.GetRequiredService<SqliteStorage>(),
            sp.GetRequiredService<IReadOnlyDictionary<AccountType, ICalendarProvider>>()));

        // ViewModels
        services.AddTransient<Views.Main.MainWindowViewModel>(sp =>
        {
            var databaseService = sp.GetRequiredService<DatabaseService>();
            var credentialManager = sp.GetRequiredService<CredentialManagerService>();
            var syncService = sp.GetRequiredService<SyncService>();
            var contactSyncService = sp.GetRequiredService<ContactSyncService>();
            var mailSyncService = sp.GetRequiredService<MailSyncService>();
            var reminderService = sp.GetRequiredService<ReminderService>();
            var calDavService = sp.GetRequiredService<CalDavService>();
            var cardDavService = sp.GetRequiredService<CardDavService>();
            var themeService = sp.GetRequiredService<ThemeService>();
            var settingsService = sp.GetRequiredService<SettingsService>();
            var storage = sp.GetRequiredService<SqliteStorage>();
            var googleCalendarService = sp.GetRequiredService<GoogleCalendarService>();
            var googleOAuthService = sp.GetRequiredService<GoogleOAuthService>();
            var calendarSource = sp.GetRequiredService<ICalendarSource>();
            var debugFeatures = sp.GetRequiredService<DebugFeaturesService>();


            return new Views.Main.MainWindowViewModel(
                databaseService,
                credentialManager,
                syncService,
                contactSyncService,
                mailSyncService,
                reminderService,
                calDavService,
                cardDavService,
                themeService,
                settingsService,
                storage,
                googleCalendarService,
                googleOAuthService,
                calendarSource,
                debugFeatures);
        });

        return services;
    }
}
