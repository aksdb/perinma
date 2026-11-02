using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using AtomUI;
using Avalonia;
using Avalonia.Threading;
using AtomCalendar = AtomUI.Desktop.Controls.Calendar;

namespace perinma.Views.CalendarList;

/// <summary>
/// Aligns an AtomUI Calendar's week start with the main view, whose weeks always start on Monday.
/// AtomUI derives the week start from the app language's formatting culture and only exposes it
/// through the internal CurrentCulture of the Calendar, so this pins that culture to a Monday-first
/// clone of whatever culture AtomUI resolved, keeping month names and formats untouched. Clones of
/// *-US/*-TW/*-BR cultures are rebased onto a Monday-first culture of the same language because
/// AtomUI hard-codes those names to Sunday-first and ignores DateTimeFormat.FirstDayOfWeek.
/// Re-asserts when AtomUI re-applies the culture (template reapplication or language switch).
/// </summary>
internal static class MondayWeekStart
{
    private static readonly PropertyInfo? CultureProperty = typeof(AtomCalendar).GetProperty(
        "CurrentCulture", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly MethodInfo? RefreshPresentation = typeof(AtomCalendar).GetMethod(
        "RefreshPresentation", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly Dictionary<string, string> MondayFirstBases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "en-GB",
        ["zh"] = "zh-CN",
        ["pt"] = "pt-PT",
    };

    public static void Attach(AtomCalendar calendar)
    {
        calendar.TemplateApplied += (_, _) =>
        {
            Correct(calendar);
            Dispatcher.UIThread.Post(() => Correct(calendar), DispatcherPriority.Loaded);
        };

        if (Application.Current is { } application
            && ApplicationExtensions.GetLanguageManager(application) is { } languageManager)
        {
            languageManager.LanguageChanged += (_, _) =>
                Dispatcher.UIThread.Post(() => Correct(calendar), DispatcherPriority.Loaded);
        }
    }

    private static void Correct(AtomCalendar calendar)
    {
        if (CultureProperty?.GetValue(calendar) is not CultureInfo culture
            || culture.DateTimeFormat.FirstDayOfWeek == DayOfWeek.Monday)
        {
            return;
        }

        var mondayFirst = (CultureInfo)culture.Clone();
        mondayFirst.DateTimeFormat.FirstDayOfWeek = DayOfWeek.Monday;
        if (IsSundayFirstSpecialCase(mondayFirst)
            && MondayFirstBases.TryGetValue(culture.TwoLetterISOLanguageName, out var baseName))
        {
            // AtomUI hard-codes *-US/*-TW/*-BR to Sunday-first regardless of DateTimeFormat,
            // so rebase onto a Monday-first culture of the same language while keeping the
            // resolved culture's date/time formats untouched.
            var baseCulture = (CultureInfo)new CultureInfo(baseName).Clone();
            baseCulture.DateTimeFormat = (DateTimeFormatInfo)culture.DateTimeFormat.Clone();
            baseCulture.DateTimeFormat.FirstDayOfWeek = DayOfWeek.Monday;
            baseCulture.DateTimeFormat.CalendarWeekRule = CalendarWeekRule.FirstDay;
            mondayFirst = baseCulture;
        }

        CultureProperty.GetSetMethod(true)?.Invoke(calendar, [mondayFirst]);
        RefreshPresentation?.Invoke(calendar, null);
    }

    private static bool IsSundayFirstSpecialCase(CultureInfo culture) =>
        culture.Name.EndsWith("-US", StringComparison.OrdinalIgnoreCase)
        || culture.Name.EndsWith("-TW", StringComparison.OrdinalIgnoreCase)
        || culture.Name.EndsWith("-BR", StringComparison.OrdinalIgnoreCase);
}
