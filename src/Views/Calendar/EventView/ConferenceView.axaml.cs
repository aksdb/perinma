using Avalonia.Controls;
using Avalonia.Input;
using perinma.Utils;

namespace perinma.Views.Calendar.EventView;

public partial class ConferenceView : UserControl
{
    public ConferenceView()
    {
        InitializeComponent();
    }

    private void OnUriPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not TextBlock textBlock || e.GetCurrentPoint(textBlock).Properties.IsLeftButtonPressed != true)
            return;

        if (textBlock.DataContext is ConferenceEntryPointViewModel viewModel)
        {
            PlatformUtil.OpenBrowser(viewModel.Uri);
            e.Handled = true;
        }
    }
}
