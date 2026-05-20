using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using perinma.Models;

namespace perinma.Views.Calendar.EventEdit;

public partial class RecurrenceActionDialog : AtomUI.Desktop.Controls.Window
{
    public RecurrenceActionDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is RecurrenceActionDialogViewModel vm)
            {
                vm.CloseRequested += action => Close(action);
            }
        };
    }
}
