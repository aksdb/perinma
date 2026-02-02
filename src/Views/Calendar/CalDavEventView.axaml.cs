using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace perinma.Views.Calendar;

public partial class CalDavEventView : UserControl
{
    public CalDavEventView()
    {
        InitializeComponent();
    }

    private void OnAttendeePointerEntered(object? sender, PointerEventArgs e)
    {
        // Find the parent Border that has the flyout attached
        if (sender is Grid grid && grid.Parent is Border border)
        {
            FlyoutBase.ShowAttachedFlyout(border);
        }
    }
}
