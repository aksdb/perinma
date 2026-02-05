using System;
using Avalonia.Controls;

namespace perinma.Views.Calendar;

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
