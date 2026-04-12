using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using perinma.Views.Calendar;

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
        DataContextChanged += OnDataContextChanged;
        SidebarCalendar.LayoutUpdated += OnSidebarLayoutUpdated;
        SidebarCalendar.DisplayDateChanged += (_, _) => InvalidateHighlight();
    }

    private void OnSidebarLayoutUpdated(object? sender, System.EventArgs e) => UpdateHighlight();

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is CalendarListViewModel oldVm)
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;

        if (DataContext is CalendarListViewModel newVm)
            newVm.PropertyChanged += OnViewModelPropertyChanged;

        UpdateHighlight();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CalendarListViewModel.ActiveCalendarViewModel))
        {
            if (sender is CalendarListViewModel vm && vm.ActiveCalendarViewModel is CalendarViewModelBase newBase)
                newBase.PropertyChanged += OnActiveCalendarPropertyChanged;
            UpdateHighlight();
        }
    }

    private void OnActiveCalendarPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CalendarViewModelBase.ViewStart)
            or nameof(CalendarViewModelBase.HighlightStart)
            or nameof(CalendarViewModelBase.HighlightEnd))
            UpdateHighlight();
    }

    private (DateTime? Start, DateTime? End) _lastHighlight;
    private DateTime _lastDisplayMonth;

    private void InvalidateHighlight()
    {
        _lastHighlight = default;
        _lastDisplayMonth = default;
        UpdateHighlight();
    }

    private void UpdateHighlight()
    {
        if (DataContext is not CalendarListViewModel vm || vm.ActiveCalendarViewModel is not CalendarViewModelBase activeVm)
            return;

        var highlightStart = activeVm.HighlightStart;
        var highlightEnd = activeVm.HighlightEnd;
        var displayMonth = new DateTime(SidebarCalendar.DisplayDate.Year, SidebarCalendar.DisplayDate.Month, 1);

        if (highlightStart == _lastHighlight.Start && highlightEnd == _lastHighlight.End && displayMonth == _lastDisplayMonth)
            return;

        _lastHighlight = (highlightStart, highlightEnd);
        _lastDisplayMonth = displayMonth;

        if (highlightStart == null || highlightEnd == null)
        {
            foreach (var child in SidebarCalendar.GetVisualDescendants())
            {
                if (child is CalendarDayButton dayButton)
                    dayButton.Classes.Remove("highlighted");
            }
            return;
        }

        var displayDate = SidebarCalendar.DisplayDate;
        var firstOfMonth = displayDate.Date.AddDays(1 - displayDate.Day);
        var firstDayOfWeek = (int)firstOfMonth.DayOfWeek;
        var calendarFirstDayOfWeek = (int)SidebarCalendar.FirstDayOfWeek;
        var offset = (firstDayOfWeek - calendarFirstDayOfWeek + 7) % 7;
        var gridStart = firstOfMonth.AddDays(-offset);

        var start = highlightStart.Value.Date;
        var end = highlightEnd.Value.Date;
        var buttonIndex = 0;

        foreach (var child in SidebarCalendar.GetVisualDescendants())
        {
            if (child is not CalendarDayButton dayButton)
                continue;

            var dayDate = gridStart.AddDays(buttonIndex);
            if (dayDate >= start && dayDate <= end)
                dayButton.Classes.Add("highlighted");
            else
                dayButton.Classes.Remove("highlighted");

            buttonIndex++;
        }

        if (buttonIndex == 0)
            _lastHighlight = (null, null);
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

    private void ColorBox_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border colorBox)
            return;

        if (colorBox.DataContext is not CalendarViewModel calendar)
            return;

        if (!e.GetCurrentPoint(colorBox).Properties.IsLeftButtonPressed)
            return;

        calendar.Enabled = !calendar.Enabled;
        e.Handled = true;
    }
}
