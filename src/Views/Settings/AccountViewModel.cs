using CommunityToolkit.Mvvm.ComponentModel;

namespace perinma.Views.Settings;

public enum AccountType
{
    Google,
}

public partial class AccountViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private AccountType _type;
}
