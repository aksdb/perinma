using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace perinma.Views.CalendarList;

public partial class CalendarListView : UserControl
{
    private int _draggedItemIndex = -1;
    private bool _isDragging;

    public CalendarListView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void AccountGroup_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border dragHandle)
            return;
        if (DataContext is not CalendarListViewModel viewModel)
            return;

        // Find the parent border with AccountGroupViewModel
        var accountBorder = FindAccountGroupBorder(dragHandle);
        if (accountBorder?.DataContext is not AccountGroupViewModel accountGroup)
            return;

        // Only start drag on left mouse button
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        _draggedItemIndex = viewModel.AccountGroups.IndexOf(accountGroup);
        _isDragging = false;
        e.Handled = true;
    }

    private async void AccountGroup_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggedItemIndex < 0 || _isDragging)
            return;

        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            _draggedItemIndex = -1;
            return;
        }

        _isDragging = true;

        var dragData = new DataTransfer();
        dragData.Add(DataTransferItem.CreateText(_draggedItemIndex.ToString()));

        try
        {
            await DragDrop.DoDragDropAsync(e, dragData, DragDropEffects.Move);
        }
        finally
        {
            _draggedItemIndex = -1;
            _isDragging = false;
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.None;

        if (!e.DataTransfer.Contains(DataFormat.Text))
            return;

        // Find the target border under the cursor
        var targetBorder = FindAccountGroupBorder(e.Source as Visual);
        if (targetBorder?.DataContext is AccountGroupViewModel)
        {
            e.DragEffects = DragDropEffects.Move;
        }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not CalendarListViewModel viewModel)
            return;
        
        if (!e.DataTransfer.Contains(DataFormat.Text))
            return;

        if (!int.TryParse(e.DataTransfer.TryGetText(), out var sourceIndex))
            return;

        // Find the target border under the cursor
        var targetBorder = FindAccountGroupBorder(e.Source as Visual);
        if (targetBorder?.DataContext is not AccountGroupViewModel targetItem)
            return;
        
        var targetIndex = viewModel.AccountGroups.IndexOf(targetItem);
        
        if (sourceIndex == targetIndex || sourceIndex < 0 || targetIndex < 0)
            return;

        viewModel.AccountGroups.Move(sourceIndex, targetIndex);
    }

    private Border? FindAccountGroupBorder(Visual? visual)
    {
        while (visual != null)
        {
            // Look for a Border whose DataContext is AccountGroupViewModel
            // and that has DragDrop.AllowDrop set (the outer container)
            if (visual is Border border && 
                border.DataContext is AccountGroupViewModel &&
                DragDrop.GetAllowDrop(border))
            {
                return border;
            }

            visual = visual.GetVisualParent();
        }
        return null;
    }
}
