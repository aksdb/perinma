using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace perinma.Views.Settings;

public enum AccountType
{
    Google,
    CalDav
}

public partial class AccountViewModel : ViewModelBase
{
    [ObservableProperty]
    private Guid _id;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private AccountType _type;
}
