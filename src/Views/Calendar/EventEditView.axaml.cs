using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace perinma.Views.Calendar;

public class DateTimeToTimeSpanConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DateTime dateTime)
        {
            return dateTime.TimeOfDay;
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is TimeSpan timeSpan && parameter is DateTime originalDateTime)
        {
            return originalDateTime.Date.Add(timeSpan);
        }
        return null;
    }
}

public partial class EventEditView : Window
{
    public EventEditView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is EventEditViewModel viewModel)
        {
            viewModel.RequestClose += (s, args) => Close();
        }
    }
}
