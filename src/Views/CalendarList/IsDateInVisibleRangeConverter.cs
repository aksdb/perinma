using System;
using System.Collections.Generic;
using System.Globalization;
using AtomUI.Desktop.Controls;
using Avalonia.Data.Converters;

namespace perinma.Views.CalendarList;

/// <summary>
/// Decides whether a sidebar calendar day cell lies inside the period visible in the main view.
/// Bindings: cell date, HighlightStart, HighlightEnd, ViewStart, cell type.
/// </summary>
public class IsDateInVisibleRangeConverter : IMultiValueConverter
{
    public static readonly IsDateInVisibleRangeConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is not { Count: 5 }
            || values[0] is not DateTime date
            || values[3] is not DateTime viewStart
            || values[4] is not CalendarCellType { } cellType
            || cellType != CalendarCellType.Date)
        {
            return false;
        }

        var start = (values[1] as DateTime? ?? viewStart).Date;
        var end = (values[2] as DateTime? ?? values[1] as DateTime? ?? viewStart).Date;
        if (end < start)
        {
            (start, end) = (end, start);
        }

        var day = date.Date;
        return day >= start && day <= end;
    }
}
