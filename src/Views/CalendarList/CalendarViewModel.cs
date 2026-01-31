using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using perinma.Services;
using perinma.Services.CalDAV;
using perinma.Storage;
using perinma.Views.Acl;
using perinma.Views.MessageBox;

namespace perinma.Views.CalendarList;

public partial class CalendarViewModel : ViewModelBase
{
    [ObservableProperty]
    private Guid _id;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string? _color;

    [ObservableProperty]
    private bool _enabled;

    /// <summary>
    /// Gets or sets the calendar URL (ExternalId from database).
    /// </summary>
    [ObservableProperty]
    private string? _url;

    /// <summary>
    /// Gets or sets the raw ACL XML for this calendar.
    /// </summary>
    [ObservableProperty]
    private string? _aclXml;

    /// <summary>
    /// Gets or sets the raw current user privilege set XML for this calendar.
    /// </summary>
    [ObservableProperty]
    private string? _currentUserPrivilegeSetXml;

    /// <summary>
    /// Gets or sets the owner URL for this calendar.
    /// </summary>
    [ObservableProperty]
    private string? _owner;

    /// <summary>
    /// Gets or sets whether this is a CalDAV calendar.
    /// </summary>
    [ObservableProperty]
    private bool _isCalDav;

    /// <summary>
    /// Gets or sets the storage service for retrieving ACL data.
    /// </summary>
    private SqliteStorage? _storage;

    /// <summary>
    /// Gets or sets the credential manager for CalDAV authentication.
    /// </summary>
    private CredentialManagerService? _credentialManager;

    public event EventHandler<bool>? EnabledChanged;

    partial void OnEnabledChanged(bool value)
    {
        EnabledChanged?.Invoke(this, value);
    }

    /// <summary>
    /// Sets the storage service for this calendar.
    /// </summary>
    public void SetServices(SqliteStorage storage, CredentialManagerService credentialManager)
    {
        _storage = storage;
        _credentialManager = credentialManager;
    }

    /// <summary>
    /// Command to open the ACL management dialog.
    /// </summary>
    [RelayCommand]
    private async Task ManagePermissionsAsync()
    {
        if (_storage == null || _credentialManager == null || string.IsNullOrEmpty(Url))
        {
            Console.WriteLine("Cannot open ACL management: Missing services or URL");
            return;
        }

        var ownerWindow = App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (ownerWindow == null)
        {
            Console.WriteLine("Cannot open ACL management: No owner window found");
            return;
        }

        try
        {
            var result = await AclManagementWindow.ShowAsync(
                owner: ownerWindow,
                calendarUrl: Url,
                calendarName: Name,
                calendarColor: Color,
                ownerUrl: Owner,
                aclXml: AclXml,
                currentUserPrivilegeSetXml: CurrentUserPrivilegeSetXml,
                storage: _storage,
                credentialManager: _credentialManager
            );

            if (result)
            {
                Console.WriteLine($"ACL saved successfully for calendar '{Name}'");
                // Note: Calendar data will be refreshed on next sync
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error opening ACL management: {ex.Message}");
            await MessageBoxWindow.ShowAsync(
                ownerWindow,
                "Error",
                $"Failed to open ACL management: {ex.Message}",
                MessageBoxType.Error,
                MessageBoxButtons.Ok
            );
        }
    }
}
