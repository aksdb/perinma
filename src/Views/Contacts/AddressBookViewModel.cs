using System;
using CommunityToolkit.Mvvm.ComponentModel;
using perinma.Models;
using perinma.Storage.Models;

namespace perinma.Views.Contacts;

public partial class AddressBookViewModel : ObservableObject
{
    public AddressBookViewModel(AddressBookQueryResult addressBook)
    {
        AddressBookId = Guid.Parse(addressBook.AddressBookId);
        Name = addressBook.Name;
        Enabled = addressBook.IsEnabled;
        ContactCount = addressBook.ContactCount;
        AccountType = addressBook.AccountTypeEnum;
    }

    [ObservableProperty]
    private Guid _addressBookId;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private bool _enabled;

    [ObservableProperty]
    private int _contactCount;

    [ObservableProperty]
    private AccountType _accountType;

    public event EventHandler<bool>? EnabledChanged;

    partial void OnEnabledChanged(bool value)
    {
        EnabledChanged?.Invoke(this, value);
    }
}
