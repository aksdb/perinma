using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;

namespace perinma.Views.Calendar.Availability;

/// <summary>
/// Custom Avalonia control that renders the availability timeline grid.
/// Each participant row shows busy slots as coloured rectangles.
///
/// The organizer row (<see cref="ParticipantAvailabilityViewModel.IsOrganizerRow"/>) is
/// rendered using per-event calendar colours sourced from <see cref="ParticipantAvailabilityViewModel.OwnEvents"/>.
/// Hovering over an own-event rectangle shows a tooltip with the event title.
///
/// A separate draggable overlay rectangle (the proposed event slot) is NOT drawn here —
/// it is rendered as an ordinary Border/Canvas in the parent AXAML so pointer events
/// remain straightforward to wire up.
///
/// Layout dimensions are exposed as public constants so the parent window can align its
/// name column and overlay canvas to match.
/// </summary>
public class AvailabilityTimelineControl : Control
{
    public const double RowHeight   = 28;
    public const double RowSpacing  = 4;
    public const double HeaderHeight = 20;
    private const double CornerRadius  = 2;
    private const double MinSlotWidth  = 2;

    // ── Styled properties (bindable from AXAML) ───────────────────────────────

    public static readonly StyledProperty<IReadOnlyList<ParticipantAvailabilityViewModel>?> RowsProperty =
        AvaloniaProperty.Register<AvailabilityTimelineControl, IReadOnlyList<ParticipantAvailabilityViewModel>?>(
            nameof(Rows));

    public static readonly StyledProperty<IReadOnlyList<TimeLabel>?> TimeLabelsProperty =
        AvaloniaProperty.Register<AvailabilityTimelineControl, IReadOnlyList<TimeLabel>?>(
            nameof(TimeLabels));

    public IReadOnlyList<ParticipantAvailabilityViewModel>? Rows
    {
        get => GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    public IReadOnlyList<TimeLabel>? TimeLabels
    {
        get => GetValue(TimeLabelsProperty);
        set => SetValue(TimeLabelsProperty, value);
    }

    // ── Static constructor — subscribe to property changes ────────────────────

    static AvailabilityTimelineControl()
    {
        RowsProperty.Changed.AddClassHandler<AvailabilityTimelineControl>(
            (c, _) => c.OnRowsChanged());
        TimeLabelsProperty.Changed.AddClassHandler<AvailabilityTimelineControl>(
            (c, _) => c.InvalidateVisual());
        AffectsRender<AvailabilityTimelineControl>(RowsProperty, TimeLabelsProperty);
    }

    // ── Layout ────────────────────────────────────────────────────────────────

    protected override Size MeasureOverride(Size availableSize)
    {
        var rowCount = Rows?.Count ?? 0;
        var height   = HeaderHeight + rowCount * (RowHeight + RowSpacing);
        // Inside a horizontal ScrollViewer the available width is PositiveInfinity.
        // MeasureOverride must return a finite size; Render uses Bounds.Width which
        // is assigned at arrange time and will reflect the actual allocated width.
        var width = double.IsInfinity(availableSize.Width) ? 480.0 : availableSize.Width;
        return new Size(width, height);
    }

    // ── Rendering ─────────────────────────────────────────────────────────────

    public override void Render(DrawingContext context)
    {
        var width  = Bounds.Width;
        var rows   = Rows;
        var labels = TimeLabels;

        if (width <= 0) return;

        var busyBrush       = TryGetResource<IBrush>("AvailabilityBusyBrush")
                              ?? new SolidColorBrush(Color.FromArgb(180, 220, 60, 60));
        var unknownBrush    = TryGetResource<IBrush>("AvailabilityUnknownBrush")
                              ?? new SolidColorBrush(Color.FromArgb(60, 128, 128, 128));
        var headerLineBrush = TryGetResource<IBrush>("SystemChromeHighColor")  ?? Brushes.Gray;
        var rowBgBrush      = TryGetResource<IBrush>("SystemChromeLowColor")
                              ?? new SolidColorBrush(Color.FromArgb(25, 128, 128, 128));
        var labelBrush      = TryGetResource<IBrush>("SystemControlForegroundBaseMediumBrush")
                              ?? Brushes.Gray;
        var typeface        = new Typeface(FontFamily.Default);

        // ── Header: time labels ───────────────────────────────────────────────

        if (labels != null)
        {
            foreach (var lbl in labels)
            {
                var x = lbl.Fraction * width;
                context.DrawLine(
                    new Pen(headerLineBrush, 1),
                    new Point(x, HeaderHeight - 4),
                    new Point(x, HeaderHeight));

                var ft = new FormattedText(lbl.Text, CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, typeface, 10, labelBrush);
                context.DrawText(ft, new Point(x - ft.Width / 2, 2));
            }
        }

        if (rows == null) return;

        // ── Participant rows ──────────────────────────────────────────────────

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var y   = HeaderHeight + i * (RowHeight + RowSpacing);

            // Row background
            context.DrawRectangle(rowBgBrush, null,
                new Rect(0, y, width, RowHeight), CornerRadius, CornerRadius);

            if (row.IsUnknown)
            {
                context.DrawRectangle(unknownBrush, null,
                    new Rect(0, y, width, RowHeight), CornerRadius, CornerRadius);
                var ft = new FormattedText("Availability unknown", CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, typeface, 10, labelBrush);
                context.DrawText(ft, new Point(4, y + (RowHeight - ft.Height) / 2));
            }
            else if (row.IsOrganizerRow)
            {
                // Own events are merged by ApplyOwnEvents; render with the same
                // colour as attendee busy blocks for visual consistency.
                foreach (var ev in row.OwnEvents)
                {
                    var slotX = ev.Start * width;
                    var slotW = Math.Max(MinSlotWidth, ev.Width * width);
                    context.DrawRectangle(busyBrush, null,
                        new Rect(slotX, y + 2, slotW, RowHeight - 4),
                        CornerRadius, CornerRadius);
                }
            }
            else
            {
                // Attendee rows: generic busy colour
                foreach (var range in row.BusyRanges)
                {
                    var slotX = range.Start * width;
                    var slotW = Math.Max(MinSlotWidth, range.Width * width);
                    context.DrawRectangle(busyBrush, null,
                        new Rect(slotX, y + 2, slotW, RowHeight - 4),
                        CornerRadius, CornerRadius);
                }
            }
        }
    }

    // ── Hover tooltip for own events ──────────────────────────────────────────

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        ToolTip.SetTip(this, FindOwnEventTitleAtPosition(e.GetPosition(this)));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        ToolTip.SetTip(this, null);
    }

    internal string? FindOwnEventTitleAtPosition(Point pos)
    {
        var rows  = Rows;
        var width = Bounds.Width;
        if (rows == null || width <= 0) return null;

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (!row.IsOrganizerRow) continue;

            var y = HeaderHeight + i * (RowHeight + RowSpacing);
            if (pos.Y < y || pos.Y > y + RowHeight) continue;

            foreach (var ev in row.OwnEvents)
            {
                var slotX = ev.Start * width;
                var slotW = Math.Max(MinSlotWidth, ev.Width * width);
                if (pos.X >= slotX && pos.X <= slotX + slotW)
                    return string.Join("\n", ev.Titles);
            }
        }

        return null;
    }

    // ── Height helpers (used by parent to size overlay canvas) ────────────────

    public static double TotalHeight(int rowCount) =>
        HeaderHeight + rowCount * (RowHeight + RowSpacing);

    public static double RowTopY(int index) =>
        HeaderHeight + index * (RowHeight + RowSpacing);

    // ── Private helpers ───────────────────────────────────────────────────────

    private void OnRowsChanged()
    {
        if (Rows is System.Collections.ObjectModel.ObservableCollection<ParticipantAvailabilityViewModel> oc)
            oc.CollectionChanged += OnCollectionChanged;

        if (Rows != null)
            foreach (var row in Rows)
                SubscribeRow(row);

        InvalidateMeasure();
        InvalidateVisual();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (ParticipantAvailabilityViewModel row in e.NewItems)
                SubscribeRow(row);
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void SubscribeRow(ParticipantAvailabilityViewModel row)
    {
        row.BusyRanges.CollectionChanged += (_, _) => InvalidateVisual();
        row.OwnEvents.CollectionChanged  += (_, _) => InvalidateVisual();
        row.PropertyChanged              += (_, _) => InvalidateVisual();
    }

    private T? TryGetResource<T>(string key) where T : class
    {
        if (Application.Current?.TryGetResource(key, ActualThemeVariant, out var res) == true
            && res is T typed)
            return typed;
        return null;
    }
}
