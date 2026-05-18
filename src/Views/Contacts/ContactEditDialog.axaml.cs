using System.Threading.Tasks;
using Avalonia.Controls;

namespace perinma.Views.Contacts;

public partial class ContactEditDialog : Window
{
    public ContactEditDialog()
    {
        InitializeComponent();
    }

    public static async Task<ContactEditResult?> ShowAsync(Window owner, ContactEditViewModel viewModel)
    {
        var dialog = new ContactEditDialog
        {
            DataContext = viewModel
        };

        viewModel.CloseRequested += dialog.Close;
        try
        {
            return await dialog.ShowDialog<ContactEditResult?>(owner);
        }
        finally
        {
            viewModel.CloseRequested -= dialog.Close;
        }
    }
}
