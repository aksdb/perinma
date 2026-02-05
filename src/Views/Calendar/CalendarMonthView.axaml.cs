using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using perinma.Models;
using perinma.Storage;
using perinma.Services;
using perinma.Views.MessageBox;

namespace perinma.Views.Calendar;

public partial class CalendarMonthView : UserControl
{
    private CalendarWeekViewModel? _viewModel;

    public CalendarMonthView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _viewModel = (CalendarWeekViewModel?)DataContext;
    }

    private void OnCreateNewEvent(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;

        var onCompleted = new Action<string>(async (errorMessage) =>
        {
            if (!string.IsNullOrEmpty(errorMessage))
            {
                await MessageBoxWindow.ShowAsync(
                    null,
                    "Error",
                    errorMessage,
                    MessageBoxType.Error,
                    MessageBoxButtons.Ok);
            }
            else
            {
                _viewModel.Load();
            }
        });

        var editor = new EventEditView
        {
            DataContext = new EventEditViewModel(
                null,
                null,
                _viewModel.Storage!,
                _viewModel.Providers,
                onCompleted)
        };
        editor.Show();
    }
}
