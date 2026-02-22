using System;
using Avalonia.Controls;
using perinma.Models;
using perinma.Views.MessageBox;

namespace perinma.Views.Calendar;

public partial class CalendarAgendaView : UserControl
{
    private CalendarWeekViewModel? _viewModel;

    public CalendarAgendaView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _viewModel = (CalendarWeekViewModel?)DataContext;
    }
}
