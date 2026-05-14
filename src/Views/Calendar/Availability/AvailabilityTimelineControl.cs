using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using perinma.Services;

namespace perinma.Views.Calendar.Availability;

/// <summary>
/// Custom Avalonia control that renders the availability timeline grid.
/// Each participant row shows busy slots as coloured rectangles.
/// A separate draggable overlay rectangle (the proposed event slot) is NOT drawn
/// here — it is rendered as an ordinary Border/Canvas in the parent AXAML so
/// pointer events remain straightforward to wire up.
///
/// Layout dimensions are exposed as public constants so the parent window can
/// align its name-column and overlay canvas to match.
/// </summary>
public class AvailabilityTimelineControl : Control
{
    public const double RowHeight = 28;
    public const double RowSpacing = 4;
    public const double HeaderHeight = 20;
    private const double CornerRadius = 2;
    private const double MinSlotWidth = 2;

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
        var height = HeaderHeight
                     + rowCount * (RowHeight + RowSpacing);
        return new Size(availableSize.Width, height);
    }

    // ── Rendering ─────────────────────────────────────────────────────────────

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        var rows = Rows;
        var labels = TimeLabels;

        if (width <= 0) return;

        // Brushes — look them up from the application theme resources
        var busyBrush = TryGetResource<IBrush>("AvailabilityBusyBrush")
                        ?? new SolidColorBrush(Color.FromArgb(180, 220, 60, 60));
        var unknownBrush = TryGetResource<IBrush>("AvailabilityUnknownBrush")
                           ?? new SolidColorBrush(Color.FromArgb(60, 128, 128, 128));
        var headerLineBrush = TryGetResource<IBrush>("SystemChromeHighColor")
                              ?? Brushes.Gray;
        var rowBgBrush = TryGetResource<IBrush>("SystemChromeLowColor")
                         ?? new SolidColorBrush(Color.FromArgb(25, 128, 128, 128));
        var labelBrush = TryGetResource<IBrush>("SystemControlForegroundBaseMediumBrush")
                         ?? Brushes.Gray;
        var headerLabelTypeface = new Typeface(FontFamily.Default);

        // Header: time labels
        if (labels != null)
        {
            foreach (var lbl in labels)
            {
                var x = lbl.Fraction * width;
                // tick mark
                context.DrawLine(
                    new Pen(headerLineBrush, 1),
                    new Point(x, HeaderHeight - 4),
                    new Point(x, HeaderHeight));
                // label text
                var ft = new FormattedText(
                    lbl.Text,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    headerLabelTypeface,
                    10,
                    labelBrush);
                context.DrawText(ft, new Point(x - ft.Width / 2, 2));
            }
        }

        if (rows == null) return;

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var y = HeaderHeight + i * (RowHeight + RowSpacing);

            // Row background
            context.DrawRectangle(rowBgBrush, null,
                new Rect(0, y, width, RowHeight), CornerRadius, CornerRadius);

            if (row.IsUnknown)
            {
                // Gray hatched overlay + label
                context.DrawRectangle(unknownBrush, null,
                    new Rect(0, y, width, RowHeight), CornerRadius, CornerRadius);
                var ft = new FormattedText(
                    "Availability unknown",
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    headerLabelTypeface,
                    10,
                    labelBrush);
                context.DrawText(ft, new Point(4, y + (RowHeight - ft.Height) / 2));
            }
            else
            {
                // Busy slot rectangles
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

    // ── Height helper (used by parent to size overlay canvas) ─────────────────

    /// <summary>
    /// Total height required for the given number of participant rows,
    /// including the header.
    /// </summary>
    public static double TotalHeight(int rowCount) =>
        HeaderHeight + rowCount * (RowHeight + RowSpacing);

    /// <summary>
    /// Top Y offset of participant row <paramref name="index"/> (0-based).
    /// </summary>
    public static double RowTopY(int index) =>
        HeaderHeight + index * (RowHeight + RowSpacing);

    // ── Private helpers ───────────────────────────────────────────────────────

    private void OnRowsChanged()
    {
        // Subscribe to collection-changed on any ObservableCollection<> row lists
        // so new rows or updated busy ranges trigger a re-render.
        if (Rows is System.Collections.ObjectModel.ObservableCollection<ParticipantAvailabilityViewModel> oc)
            oc.CollectionChanged += OnCollectionChanged;

        if (Rows != null)
        {
            foreach (var row in Rows)
                SubscribeRow(row);
        }

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
        row.PropertyChanged += (_, _) => InvalidateVisual();
    }

    private T? TryGetResource<T>(string key) where T : class
    {
        if (Application.Current?.TryGetResource(key, ActualThemeVariant, out var res) == true
            && res is T typed)
            return typed;
        return null;
    }
}
