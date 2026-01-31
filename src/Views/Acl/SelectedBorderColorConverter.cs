using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace perinma.Views.Acl;

/// <summary>
/// Converts boolean isSelected to border color.
/// </summary>
public class SelectedBorderColorConverter : IValueConverter
{
    public static readonly SelectedBorderColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isSelected && isSelected)
        {
            return new SolidColorBrush(Color.FromRgb(0, 120, 212)); // Blue selection color
        }

        return new SolidColorBrush(Colors.Transparent);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
