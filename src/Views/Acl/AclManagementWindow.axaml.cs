using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using perinma.Storage;
using perinma.Services;

namespace perinma.Views.Acl;

/// <summary>
/// Window for managing CalDAV ACL (Access Control List) permissions.
/// </summary>
public partial class AclManagementWindow : Window
{
    public AclManagementWindow()
    {
        InitializeComponent();

        // Subscribe to close request from ViewModel
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is AclManagementViewModel viewModel)
        {
            viewModel.CloseRequested += (_, _) => Close();
        }
    }

    private void OnAceBorderPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (sender is Border border && border.Tag is AceItemViewModel ace)
        {
            ace.SelectCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Shows the ACL management window for a specific calendar.
    /// </summary>
    /// <param name="owner">The owner window.</param>
    /// <param name="calendarUrl">The URL of the calendar.</param>
    /// <param name="calendarName">The name of the calendar.</param>
    /// <param name="calendarColor">The color of the calendar.</param>
    /// <param name="ownerUrl">The owner URL (optional).</param>
    /// <param name="aclXml">The raw ACL XML (optional, will be fetched if not provided).</param>
    /// <param name="currentUserPrivilegeSetXml">The current user privileges XML (optional).</param>
    /// <param name="storage">The storage service.</param>
    /// <param name="credentialManager">The credential manager service.</param>
    /// <returns>A task that completes when the window is closed.</returns>
    public static Task<bool> ShowAsync(
        Window owner,
        string calendarUrl,
        string calendarName,
        string? calendarColor = null,
        string? ownerUrl = null,
        string? aclXml = null,
        string? currentUserPrivilegeSetXml = null,
        SqliteStorage? storage = null,
        Services.CredentialManagerService? credentialManager = null)
    {
        var window = new AclManagementWindow();

        // Initialize ViewModel with calendar data
        var viewModel = new AclManagementViewModel(
            owner,
            calendarUrl,
            calendarName,
            calendarColor,
            ownerUrl,
            aclXml,
            currentUserPrivilegeSetXml,
            storage ?? throw new ArgumentNullException(nameof(storage)),
            credentialManager ?? throw new ArgumentNullException(nameof(credentialManager))!);

        window.DataContext = viewModel;

        var tcs = new TaskCompletionSource<bool>();
        window.Closed += (_, _) => tcs.TrySetResult(viewModel.WasSaved);

        window.ShowDialog(owner);

        return tcs.Task;
    }
}
