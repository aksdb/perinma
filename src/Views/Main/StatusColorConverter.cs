using System;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace perinma.Views.Main;

public class StatusColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is bool isError)
        {
            return isError ? Color.Parse("#A80000") : Color.Parse("#666666");
        }
        return Colors.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
