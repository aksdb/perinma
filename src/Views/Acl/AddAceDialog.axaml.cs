using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using perinma.Storage;
using perinma.Services;
using perinma.Services.CalDAV;

namespace perinma.Views.Acl;

/// <summary>
/// Dialog for adding a new ACE (Access Control Entry).
/// </summary>
public partial class AddAceDialog : Window
{
    public AddAceDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Shows the dialog with services for principal search.
    /// </summary>
    public static async Task<WebDavAce?> ShowAsync(
        Window owner,
        SqliteStorage storage,
        CredentialManagerService credentialManager,
        Models.Calendar? calendar,
        string? principalCollectionUrl = null)
    {
        var dialog = new AddAceDialog();

        // Initialize ViewModel with services
        var viewModel = new AddAceDialogViewModel(
            dialog,
            storage,
            credentialManager,
            calendar,
            principalCollectionUrl);

        dialog.DataContext = viewModel;

        var result = await dialog.ShowDialog<AceItemViewModel?>(owner);

        // Return the WebDavAce from the AceItemViewModel
        return result?.ToWebDavAce();
    }

    private void OnSearchResultPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is PrincipalSearchResult principal)
        {
            if (DataContext is AddAceDialogViewModel viewModel)
            {
                viewModel.SelectPrincipal(principal);
                e.Handled = true;
            }
        }
    }
}
