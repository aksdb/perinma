using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using AtomUI.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using perinma.Models;
using perinma.Storage;

namespace perinma.Views.Settings.AddAccountWizard;

public partial class AccountDetailsStepViewModel : ObservableValidator
{
    private readonly SqliteStorage _storage;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "Account name is required")]
    [MinLength(1, ErrorMessage = "Account name cannot be empty")]
    private string _accountName = string.Empty;

    [ObservableProperty]
    private string? _nameValidationError;

    [ObservableProperty]
    private FormValidateStatus _accountNameValidateStatus;

    [ObservableProperty]
    private string? _capabilityValidationError;

    [ObservableProperty]
    private FormValidateStatus _capabilityValidateStatus;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditCapabilities))]
    [NotifyPropertyChangedFor(nameof(SelectedCapabilities))]
    private AccountType _selectedAccountType = AccountType.Google;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedCapabilities))]
    private bool _includeCalendar = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedCapabilities))]
    private bool _includeContacts = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedCapabilities))]
    private bool _includeMail = true;

    public AccountDetailsStepViewModel(SqliteStorage storage)
    {
        _storage = storage;
        ApplyDefaultCapabilities(SelectedAccountType);
    }

    public bool CanEditCapabilities => SelectedAccountType == AccountType.Google;

    public AccountCapability SelectedCapabilities
    {
        get
        {
            var capabilities = AccountCapability.None;
            if (IncludeCalendar)
            {
                capabilities |= AccountCapability.Calendar;
            }

            if (IncludeContacts)
            {
                capabilities |= AccountCapability.Contacts;
            }

            if (IncludeMail)
            {
                capabilities |= AccountCapability.Mail;
            }

            return capabilities;
        }
    }

    partial void OnAccountNameChanged(string value)
    {
        ClearAccountNameValidation();
    }

    partial void OnSelectedAccountTypeChanged(AccountType value)
    {
        ApplyDefaultCapabilities(value);
        ClearCapabilityValidation();
    }

    partial void OnIncludeCalendarChanged(bool value)
    {
        if (!CanEditCapabilities)
        {
            return;
        }

        ClearCapabilityValidation();
    }

    partial void OnIncludeContactsChanged(bool value)
    {
        if (!CanEditCapabilities)
        {
            return;
        }

        ClearCapabilityValidation();
    }

    partial void OnIncludeMailChanged(bool value)
    {
        if (!CanEditCapabilities)
        {
            return;
        }

        ClearCapabilityValidation();
    }

    public async Task<bool> ValidateAsync()
    {
        ValidateAllProperties();
        if (HasErrors)
        {
            SetAccountNameValidationError(
                GetErrors(nameof(AccountName))
                    .Cast<ValidationResult>()
                    .FirstOrDefault()?.ErrorMessage ?? "Account name is invalid");
            return false;
        }

        if (SelectedCapabilities == AccountCapability.None)
        {
            SetCapabilityValidationError("Select at least one capability for this account");
            return false;
        }

        var isUnique = await _storage.IsAccountNameUniqueAsync(AccountName);
        if (!isUnique)
        {
            SetAccountNameValidationError("An account with this name already exists");
            return false;
        }

        NameValidationError = null;
        AccountNameValidateStatus = FormValidateStatus.Success;
        ClearCapabilityValidation();
        CapabilityValidateStatus = FormValidateStatus.Success;
        return true;
    }

    private void ApplyDefaultCapabilities(AccountType accountType)
    {
        switch (accountType)
        {
            case AccountType.Google:
                IncludeCalendar = true;
                IncludeContacts = true;
                IncludeMail = true;
                break;
            case AccountType.CalDav:
                IncludeCalendar = true;
                IncludeContacts = false;
                IncludeMail = false;
                break;
            case AccountType.CardDav:
                IncludeCalendar = false;
                IncludeContacts = true;
                IncludeMail = false;
                break;
            case AccountType.Jmap:
                IncludeCalendar = false;
                IncludeContacts = false;
                IncludeMail = true;
                break;
        }
    }

    private void ClearAccountNameValidation()
    {
        NameValidationError = null;
        AccountNameValidateStatus = FormValidateStatus.Default;
    }

    private void SetAccountNameValidationError(string message)
    {
        NameValidationError = message;
        AccountNameValidateStatus = FormValidateStatus.Error;
    }

    private void ClearCapabilityValidation()
    {
        CapabilityValidationError = null;
        CapabilityValidateStatus = FormValidateStatus.Default;
    }

    private void SetCapabilityValidationError(string message)
    {
        CapabilityValidationError = message;
        CapabilityValidateStatus = FormValidateStatus.Error;
    }
}
