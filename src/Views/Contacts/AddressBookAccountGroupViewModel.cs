using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using perinma.Models;

namespace perinma.Views.Contacts;

public partial class AddressBookAccountGroupViewModel : ObservableObject
{
    [ObservableProperty]
    private Guid _accountId;

    [ObservableProperty]
    private string _accountName = string.Empty;

    [ObservableProperty]
    private AccountType _accountType;

    [ObservableProperty]
    private bool _isExpanded = true;

    public ObservableCollection<AddressBookViewModel> AddressBooks { get; } = [];
}
