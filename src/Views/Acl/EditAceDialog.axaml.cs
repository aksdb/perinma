using Avalonia.Controls;

namespace perinma.Views.Acl;

/// <summary>
/// Dialog for editing an existing ACE (Access Control Entry).
/// </summary>
public partial class EditAceDialog : Window
{
    public EditAceDialog(AceItemViewModel aceViewModel)
    {
        InitializeComponent();

        // Initialize ViewModel
        DataContext = new EditAceDialogViewModel(this, aceViewModel);
    }
}
