using Avalonia.Data.Converters;
using Avalonia.Media;

namespace perinma.Views.Acl;

/// <summary>
/// Converts boolean values to colors (true = green for grant, false = red for deny).
/// </summary>
public class BooleanToColorConverter : IValueConverter
{
    public object? Convert(object? value, System.Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is bool isGrant)
        {
            return Color.Parse(isGrant ? "#107C10" : "#A80000");
        }
        return Colors.Transparent;
    }

    public object? ConvertBack(object? value, System.Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        throw new System.NotImplementedException();
    }
}
