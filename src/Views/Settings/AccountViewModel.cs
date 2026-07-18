using System;
using System.Collections.Generic;
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
    [NotifyPropertyChangedFor(nameof(NeedsMailUpgrade))]
    [NotifyPropertyChangedFor(nameof(ReauthenticateLabel))]
    private AccountType _type;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CapabilitiesSummary))]
    [NotifyPropertyChangedFor(nameof(NeedsMailUpgrade))]
    [NotifyPropertyChangedFor(nameof(ReauthenticateLabel))]
    private AccountCapability _capabilities;

    [ObservableProperty]
    private bool _canReauthenticate = true;

    public bool SupportsReauthentication => Type == AccountType.Google;
    public bool NeedsMailUpgrade => Type == AccountType.Google && !Capabilities.HasFlag(AccountCapability.Mail);
    public string ReauthenticateLabel => NeedsMailUpgrade ? "Grant Mail Access" : "Reauthenticate";

    public string CapabilitiesSummary
    {
        get
        {
            if (Capabilities == AccountCapability.None)
            {
                return "No capabilities";
            }

            var parts = new List<string>(3);
            if (Capabilities.HasFlag(AccountCapability.Calendar))
            {
                parts.Add("Calendar");
            }

            if (Capabilities.HasFlag(AccountCapability.Contacts))
            {
                parts.Add("Contacts");
            }

            if (Capabilities.HasFlag(AccountCapability.Mail))
            {
                parts.Add("Mail");
            }

            return string.Join(", ", parts);
        }
    }

    public IAsyncRelayCommand ForceResyncCommand { get; init; } = null!;
    public IAsyncRelayCommand ReauthenticateCommand { get; init; } = null!;
    public IAsyncRelayCommand DeleteCommand { get; init; } = null!;
}
