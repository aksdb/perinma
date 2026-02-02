using System;
using CommunityToolkit.Mvvm.ComponentModel;
using perinma.Models;
using perinma.Storage.Models;

namespace perinma.Views.Contacts;

public partial class ContactGroupViewModel : ObservableObject
{
    public ContactGroupViewModel(ContactGroupQueryResult group)
    {
        GroupId = Guid.Parse(group.GroupId);
        ExternalId = group.ExternalId;
        Name = group.Name;
        IsSystemGroup = group.IsSystemGroup;
        AccountId = Guid.Parse(group.AccountId);
        AccountName = group.AccountName;
        AccountType = group.AccountTypeEnum;
        MemberCount = group.MemberCount;
    }

    [ObservableProperty]
    private Guid _groupId;

    [ObservableProperty]
    private string? _externalId;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private bool _isSystemGroup;

    [ObservableProperty]
    private Guid _accountId;

    [ObservableProperty]
    private string _accountName = string.Empty;

    [ObservableProperty]
    private AccountType _accountType;

    [ObservableProperty]
    private int _memberCount;
}
