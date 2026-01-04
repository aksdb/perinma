using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;

namespace perinma.Views.Settings;

public partial class AccountListViewModel : ViewModelBase
{
    [ObservableProperty]
    private AvaloniaList<AccountViewModel> _accounts = [];
}