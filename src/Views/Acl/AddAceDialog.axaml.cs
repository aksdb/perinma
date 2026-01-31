using Avalonia.Controls;

namespace perinma.Views.Acl;

/// <summary>
/// Dialog for adding a new ACE (Access Control Entry).
/// </summary>
public partial class AddAceDialog : Window
{
    public AddAceDialog()
    {
        InitializeComponent();

        // Initialize ViewModel
        DataContext = new AddAceDialogViewModel(this);
    }
}
