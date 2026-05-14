using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using perinma.Storage.Models;
using perinma.Views.Calendar.EventEdit;

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

public class ContactQueryResultToInitialsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ContactQueryResult contact)
        {
            if (!string.IsNullOrEmpty(contact.GivenName) && !string.IsNullOrEmpty(contact.FamilyName))
            {
                return $"{contact.GivenName[0]}{contact.FamilyName[0]}".ToUpper();
            }
            else if (!string.IsNullOrEmpty(contact.DisplayName))
            {
                var parts = contact.DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    return $"{parts[0][0]}{parts[1][0]}".ToUpper();
                }
                else if (parts.Length == 1 && parts[0].Length >= 1)
                {
                    return parts[0][0].ToString().ToUpper();
                }
            }
            return "?";
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
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

    private void OnSearchTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (DataContext is EventEditViewModel viewModel)
            {
                foreach (var row in viewModel.FieldRows)
                {
                    if (row.Field is ParticipantsEditViewModel participantsVm)
                    {
                        participantsVm.AddCustomParticipantCommand.Execute(null);
                        e.Handled = true;
                        return;
                    }
                }
            }
        }
    }
}
