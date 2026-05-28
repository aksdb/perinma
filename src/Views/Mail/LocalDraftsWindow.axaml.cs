using System.Threading.Tasks;
using Avalonia.Controls;

namespace perinma.Views.Mail;

public partial class LocalDraftsWindow : AtomUI.Desktop.Controls.Window
{
    public LocalDraftsWindow()
    {
        InitializeComponent();
    }

    public static async Task<string?> ShowAsync(Window owner, LocalDraftsViewModel viewModel)
    {
        var dialog = new LocalDraftsWindow
        {
            DataContext = viewModel
        };

        string? selectedDraftId = null;
        viewModel.CloseRequested += OnCloseRequested;
        try
        {
            await viewModel.InitializeAsync();
            await dialog.ShowDialog(owner);
            return selectedDraftId;
        }
        finally
        {
            viewModel.CloseRequested -= OnCloseRequested;
        }

        void OnCloseRequested(string? draftId, bool _)
        {
            selectedDraftId = draftId;
            dialog.Close();
        }
    }
}
