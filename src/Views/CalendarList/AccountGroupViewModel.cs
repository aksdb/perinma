using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace perinma.Views.CalendarList;

public partial class AccountGroupViewModel : ObservableObject
{
    [ObservableProperty]
    private Guid _accountId;

    [ObservableProperty]
    private string _accountName = string.Empty;

    public ObservableCollection<CalendarViewModel> Calendars { get; } = new();
}
