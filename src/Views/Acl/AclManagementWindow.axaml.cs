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

    public static Task<bool> ShowAsync(
        Window owner,
        Models.Calendar calendar,
        string? ownerUrl = null,
        string? aclXml = null,
        string? currentUserPrivilegeSetXml = null,
        SqliteStorage? storage = null,
        CredentialManagerService? credentialManager = null)
    {
        var window = new AclManagementWindow();

        // Initialize ViewModel with calendar data
        var viewModel = new AclManagementViewModel(
            owner,
            calendar,
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
