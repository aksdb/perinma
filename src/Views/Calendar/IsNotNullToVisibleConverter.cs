using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace perinma.Views.Calendar;

public class IsNotNullToVisibleConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is not null && value is not string;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
