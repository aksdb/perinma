using System;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace perinma.Views;

public class EventChip : TemplatedControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<EventChip, string?>(nameof(Title));

    public static readonly StyledProperty<string?> StartTextProperty =
        AvaloniaProperty.Register<EventChip, string?>(nameof(StartText));

    public static readonly StyledProperty<string?> EndTextProperty =
        AvaloniaProperty.Register<EventChip, string?>(nameof(EndText));

    public static readonly StyledProperty<IBrush?> BackgroundBrushProperty =
        AvaloniaProperty.Register<EventChip, IBrush?>(nameof(BackgroundBrush));

    public static readonly StyledProperty<IBrush?> ForegroundBrushProperty =
        AvaloniaProperty.Register<EventChip, IBrush?>(nameof(ForegroundBrush));

    public static readonly StyledProperty<bool> ShowInlineTimesProperty =
        AvaloniaProperty.Register<EventChip, bool>(nameof(ShowInlineTimes));

    public static readonly StyledProperty<bool> ShowStackedTimesProperty =
        AvaloniaProperty.Register<EventChip, bool>(nameof(ShowStackedTimes));

    public static readonly StyledProperty<string?> InlineTimeTextProperty =
        AvaloniaProperty.Register<EventChip, string?>(nameof(InlineTimeText));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? StartText
    {
        get => GetValue(StartTextProperty);
        set => SetValue(StartTextProperty, value);
    }

    public string? EndText
    {
        get => GetValue(EndTextProperty);
        set => SetValue(EndTextProperty, value);
    }

    public IBrush? BackgroundBrush
    {
        get => GetValue(BackgroundBrushProperty);
        set => SetValue(BackgroundBrushProperty, value);
    }

    public IBrush? ForegroundBrush
    {
        get => GetValue(ForegroundBrushProperty);
        set => SetValue(ForegroundBrushProperty, value);
    }

    public bool ShowInlineTimes
    {
        get => GetValue(ShowInlineTimesProperty);
        set => SetValue(ShowInlineTimesProperty, value);
    }

    public bool ShowStackedTimes
    {
        get => GetValue(ShowStackedTimesProperty);
        set => SetValue(ShowStackedTimesProperty, value);
    }

    public string? InlineTimeText
    {
        get => GetValue(InlineTimeTextProperty);
        set => SetValue(InlineTimeTextProperty, value);
    }

    // Layout helper properties used by CalendarWeekView for positioning
    public int StartSlot { get; set; }
    public int EndSlot { get; set; }
    public int DaySlot { get; set; }
    public int ColumnSlot { get; set; }
    public int TotalColumns { get; set; } = 1;

    private double _inlineTimeWidth = 0;

    public EventChip()
    {
        ShowInlineTimes = true;
        UpdateInlineText();
    }

    static EventChip()
    {
        // Recompute inline text when start or end text changes
        StartTextProperty.Changed.AddClassHandler<EventChip>((o, _) =>
        {
            o.UpdateInlineText();
            o.UpdateInlineVisibility(o.Bounds.Width);
        });
        EndTextProperty.Changed.AddClassHandler<EventChip>((o, _) =>
        {
            o.UpdateInlineText();
            o.UpdateInlineVisibility(o.Bounds.Width);
        });
        // React to bounds (width) changes to toggle inline/stacked
        BoundsProperty.Changed.AddClassHandler<EventChip>((o, _e) =>
        {
            // Use current bounds; change args type differences across Avalonia versions avoided
            o.UpdateInlineVisibility(o.Bounds.Width);
        });
    }

    private void UpdateInlineText()
    {
        var start = StartText;
        var end = EndText;
        InlineTimeText = string.IsNullOrWhiteSpace(start) switch
        {
            false when !string.IsNullOrWhiteSpace(end) => $"🕐 {start}-{end}",
            false => $"🕐 {start}",
            _ => null
        };

        CalculateInlineTextWidth();
    }

    private void CalculateInlineTextWidth()
    {
        var inlineText = InlineTimeText;
        if (string.IsNullOrWhiteSpace(inlineText))
        {
            _inlineTimeWidth = 0;
            return;
        }
        
        // TODO bind to actual control and react on that change
        var typeface = new Typeface(FontFamily, FontStyle, FontWeight);
        var fontSize = GetMinuteFontSize();
        // Create a layout for a single-line measurement without wrapping.
        var layout = new TextLayout(inlineText!, typeface, fontSize, ForegroundBrush);
        _inlineTimeWidth = layout.Width;
    }

    private void UpdateInlineVisibility(double width)
    {
        // Guard against NaN/zero
        if (double.IsNaN(width) || width <= 0)
            return;

        // Prefer dynamic measurement of the inline text over a hard threshold.
        // Estimate inner available width by subtracting the known horizontal margin (5 left + 5 right) from the template.
        var innerWidth = Math.Max(0, width - 10);

        var canShowInline = innerWidth >= _inlineTimeWidth;
       
        ShowInlineTimes = canShowInline;
        ShowStackedTimes = !canShowInline;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateInlineVisibility(Bounds.Width);
    }

    private double GetMinuteFontSize()
    {
        // Try to obtain the font size used for the time text from resources, fallback to a sensible default.
        var app = Application.Current;
        if (app != null)
        {
            try
            {
                // Avalonia 11 signature with theme variant
                if (app.TryFindResource("CalendarTimeMinuteFontSize", app.ActualThemeVariant, out var themed) && themed is double d1)
                    return d1;
            }
            catch
            {
                // ignore and try legacy overload below
            }

            if (app.TryFindResource("CalendarTimeMinuteFontSize", out var value) && value is double d2)
                return d2;
        }
        return 9d; // default as defined in App.axaml
    }
}
