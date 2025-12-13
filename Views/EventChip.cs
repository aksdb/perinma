using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

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

    // Layout helper properties used by CalendarWeekView for positioning
    public int StartSlot { get; set; }
    public int EndSlot { get; set; }
    public int DaySlot { get; set; }
    public int ColumnSlot { get; set; }
    public int TotalColumns { get; set; } = 1;
}
