using System;
using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace perinma.Views.CalendarList;

/// <summary>
/// Aligns an AtomUI Calendar's week start with the main view, whose weeks always start on Monday.
/// AtomUI derives the week start from the app language's formatting culture; this pins the
/// calendar's internal CalendarView to a Monday-first clone of whatever culture AtomUI resolved,
/// so month names and formats stay untouched. Re-asserts when AtomUI re-applies the culture.
/// </summary>
internal static class MondayWeekStart
{
    private static readonly FieldInfo? CultureField = typeof(AtomUI.Desktop.Controls.Calendar).Assembly
        .GetType("AtomUI.Desktop.Controls.Internal.Calendar.CalendarView")?
        .GetField("CultureProperty", BindingFlags.Public | BindingFlags.Static);

    public static void Attach(AtomUI.Desktop.Controls.Calendar calendar)
    {
        calendar.TemplateApplied += (_, _) =>
        {
            Correct(calendar);
            Dispatcher.UIThread.Post(() => Correct(calendar), DispatcherPriority.Loaded);
        };
    }


    private static void Correct(AtomUI.Desktop.Controls.Calendar calendar)
    {
        if (CultureField is null)
        {
            return;
        }

        var cultureProperty = (AvaloniaProperty)CultureField.GetValue(null)!;
        foreach (var descendant in calendar.GetVisualDescendants())
        {
            if (descendant.GetType().Name != "CalendarView" || descendant is not AvaloniaObject view)
            {
                continue;
            }

            var culture = view.GetValue(cultureProperty) as CultureInfo;
            if (culture is null || culture.DateTimeFormat.FirstDayOfWeek == DayOfWeek.Monday)
            {
                continue;
            }

            var mondayFirst = (CultureInfo)culture.Clone();
            mondayFirst.DateTimeFormat.FirstDayOfWeek = DayOfWeek.Monday;
            view.PropertyChanged += OnCalendarViewPropertyChanged;
            view.SetValue(cultureProperty, mondayFirst);
            return;
        }
    }

    private static void OnCalendarViewPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is not AvaloniaObject view
            || CultureField?.GetValue(null) is not AvaloniaProperty cultureProperty
            || !ReferenceEquals(e.Property, cultureProperty))
        {
            return;
        }

        // Guard against re-entry while we are setting the corrected culture ourselves.
        if (e.NewValue is CultureInfo applied && applied.DateTimeFormat.FirstDayOfWeek == DayOfWeek.Monday)
        {
            return;
        }

        if (e.NewValue is CultureInfo culture)
        {
            var mondayFirst = (CultureInfo)culture.Clone();
            mondayFirst.DateTimeFormat.FirstDayOfWeek = DayOfWeek.Monday;
            view.SetValue(cultureProperty, mondayFirst);
        }
    }
}
