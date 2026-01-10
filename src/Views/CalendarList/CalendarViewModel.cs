using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace perinma.Views.CalendarList;

public partial class CalendarViewModel : ViewModelBase
{
    [ObservableProperty]
    private Guid _id;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string? _color;

    [ObservableProperty]
    private bool _enabled;

    public event EventHandler<bool>? EnabledChanged;

    partial void OnEnabledChanged(bool value)
    {
        EnabledChanged?.Invoke(this, value);
    }
}
