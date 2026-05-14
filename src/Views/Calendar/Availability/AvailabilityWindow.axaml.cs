using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace perinma.Views.Calendar.Availability;

/// <summary>
/// Code-behind for the availability dialog.
///
/// Responsibilities:
///   • Kick off the initial freebusy refresh once the window is ready.
///   • Size and reposition the slot overlay when the timeline canvas resizes.
///   • Handle pointer drag events (move / resize the slot overlay).
///   • Return (SelectedStart, SelectedEnd) on confirm, null on cancel.
/// </summary>
public partial class AvailabilityWindow : Window
{
    private enum DragMode { None, Move, Resize }
    private DragMode _dragMode = DragMode.None;
    private double _dragStartX;
    private double _dragStartSlotFraction;
    private const double ResizeHandleWidth = 8;

    public AvailabilityWindow()
    {
        InitializeComponent();

        // Update overlay position whenever the panel width changes
        TimelinePanel.SizeChanged += (_, _) => UpdateOverlayFromViewModel();

        // Sync vertical scroll between name column and timeline
        TimelineScrollViewer.ScrollChanged += (_, e) =>
        {
            if (e.OffsetDelta.Y != 0)
                NameScrollViewer.Offset = NameScrollViewer.Offset.WithY(TimelineScrollViewer.Offset.Y);
        };
    }

    private AvailabilityWindowViewModel? Vm => DataContext as AvailabilityWindowViewModel;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (Vm is { } vm)
        {
            // Subscribe to slot changes so the overlay stays in sync when the VM moves the slot
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(AvailabilityWindowViewModel.SelectedSlotStartFraction)
                                       or nameof(AvailabilityWindowViewModel.SelectedSlotWidthFraction))
                {
                    Dispatcher.UIThread.Post(UpdateOverlayFromViewModel);
                }
            };

            // Initial overlay position
            UpdateOverlayFromViewModel();

            // Kick off the first freebusy load
            _ = vm.RefreshCommand.ExecuteAsync(null);
        }
    }

    // ── Overlay sizing ────────────────────────────────────────────────────────

    private void UpdateOverlayFromViewModel()
    {
        var vm = Vm;
        if (vm == null) return;

        var panelWidth = TimelinePanel.Bounds.Width;
        if (panelWidth <= 0) return;

        var rowCount = vm.Rows.Count;
        var totalHeight = rowCount > 0
            ? AvailabilityTimelineControl.RowTopY(rowCount) - AvailabilityTimelineControl.HeaderHeight
            : AvailabilityTimelineControl.RowHeight;

        var slotLeft = vm.SelectedSlotStartFraction * panelWidth;
        var slotWidth = Math.Max(4, vm.SelectedSlotWidthFraction * panelWidth);

        Canvas.SetLeft(SlotBorder, slotLeft);
        SlotBorder.Width = slotWidth;
        SlotBorder.Height = totalHeight;

        // Size the overlay canvas to fill the participant grid area
        OverlayCanvas.Width = panelWidth;
        OverlayCanvas.Height = totalHeight + AvailabilityTimelineControl.HeaderHeight;
    }

    // ── Pointer drag handling ─────────────────────────────────────────────────

    private void OnOverlayCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm == null) return;

        var pos = e.GetPosition(OverlayCanvas);
        var panelWidth = TimelinePanel.Bounds.Width;
        if (panelWidth <= 0) return;

        var slotLeft = Canvas.GetLeft(SlotBorder);
        var slotRight = slotLeft + SlotBorder.Bounds.Width;
        var hitResizeZone = pos.X >= slotRight - ResizeHandleWidth && pos.X <= slotRight + 2;

        if (hitResizeZone)
        {
            _dragMode = DragMode.Resize;
        }
        else if (pos.X >= slotLeft && pos.X <= slotRight)
        {
            _dragMode = DragMode.Move;
            _dragStartX = pos.X;
            _dragStartSlotFraction = Canvas.GetLeft(SlotBorder) / panelWidth;
        }
        else
        {
            // Click outside slot: move slot start to click position
            _dragMode = DragMode.Move;
            var clickFraction = pos.X / panelWidth;
            Vm.MoveSlot(clickFraction);
            UpdateOverlayFromViewModel();
            _dragStartX = pos.X;
            _dragStartSlotFraction = clickFraction;
        }

        e.Pointer.Capture(OverlayCanvas);
        e.Handled = true;
    }

    private void OnOverlayCanvasPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragMode == DragMode.None || Vm == null) return;

        var pos = e.GetPosition(OverlayCanvas);
        var panelWidth = TimelinePanel.Bounds.Width;
        if (panelWidth <= 0) return;

        var currentFraction = pos.X / panelWidth;

        if (_dragMode == DragMode.Move)
        {
            var deltaPx = pos.X - _dragStartX;
            var newStartFraction = _dragStartSlotFraction + deltaPx / panelWidth;
            Vm.MoveSlot(newStartFraction);
        }
        else if (_dragMode == DragMode.Resize)
        {
            Vm.ResizeSlot(currentFraction);
        }

        UpdateOverlayFromViewModel();
        e.Handled = true;
    }

    private void OnOverlayCanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragMode = DragMode.None;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    // ── Dialog result ─────────────────────────────────────────────────────────

    private void OnUseSlotClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Vm is { } vm)
            Close((vm.SelectedStart, vm.SelectedEnd));
    }

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(null);
    }
}
