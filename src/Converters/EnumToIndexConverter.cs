using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace perinma.Converters;

public sealed class EnumToIndexConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
        {
            return 0;
        }

        var valueType = value.GetType();
        if (!valueType.IsEnum)
        {
            throw new InvalidOperationException($"{nameof(EnumToIndexConverter)} can only convert enum values.");
        }

        return System.Convert.ToInt32(value, culture);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (!targetType.IsEnum)
        {
            throw new InvalidOperationException($"{nameof(EnumToIndexConverter)} can only convert back to enum types.");
        }

        if (value is int selectedIndex && Enum.IsDefined(targetType, selectedIndex))
        {
            return Enum.ToObject(targetType, selectedIndex);
        }

        return Enum.ToObject(targetType, 0);
    }
}
