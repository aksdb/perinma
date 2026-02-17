using Avalonia.Controls;
using Avalonia.Input;
using perinma.Utils;

namespace perinma.Views.Calendar.EventView;

public partial class AttachmentsView : UserControl
{
    public AttachmentsView()
    {
        InitializeComponent();
    }

    private void OnAttachmentPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not TextBlock textBlock || e.GetCurrentPoint(textBlock).Properties.IsLeftButtonPressed != true)
            return;

        if (textBlock.DataContext is AttachmentItemViewModel viewModel)
        {
            PlatformUtil.OpenBrowser(viewModel.Url);
            e.Handled = true;
        }
    }
}
