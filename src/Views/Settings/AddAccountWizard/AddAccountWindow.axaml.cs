using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace perinma.Views.Settings.AddAccountWizard;

public partial class AddAccountWindow : Window
{
    public AddAccountWindow()
    {
        InitializeComponent();

        // Subscribe to close request from ViewModel
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is AddAccountWizardViewModel viewModel)
        {
            viewModel.CloseRequested += (_, _) => Close();
        }
    }
}

// Converter for step indicator styling
public class StepToBoldConverter : IValueConverter
{
    public static readonly StepToBoldConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is int currentStep && parameter is string stepStr && int.TryParse(stepStr, out int step))
        {
            return currentStep == step ? FontWeight.Bold : FontWeight.Normal;
        }
        return FontWeight.Normal;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
