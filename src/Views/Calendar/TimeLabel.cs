using Avalonia;
using Avalonia.Controls.Primitives;

namespace perinma.Views.Calendar;

public class TimeLabel : TemplatedControl
{
    public static readonly StyledProperty<int> HourProperty =
        AvaloniaProperty.Register<TimeLabel, int>(nameof(Hour));

    public static readonly StyledProperty<int> MinuteProperty =
        AvaloniaProperty.Register<TimeLabel, int>(nameof(Minute));

    public int Hour
    {
        get => GetValue(HourProperty);
        set => SetValue(HourProperty, value);
    }

    public int Minute
    {
        get => GetValue(MinuteProperty);
        set => SetValue(MinuteProperty, value);
    }
}
