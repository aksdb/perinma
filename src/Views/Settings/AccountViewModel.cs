using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using perinma.Models;

namespace perinma.Views.Settings;

public partial class AccountViewModel : ViewModelBase
{
    [ObservableProperty]
    private Guid _id;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SupportsReauthentication))]
    private AccountType _type;

    [ObservableProperty]
    private bool _canReauthenticate = true;

    public bool SupportsReauthentication => Type == AccountType.Google;

    public IAsyncRelayCommand ForceResyncCommand { get; init; } = null!;
    public IAsyncRelayCommand ReauthenticateCommand { get; init; } = null!;
    public IAsyncRelayCommand DeleteCommand { get; init; } = null!;
}
