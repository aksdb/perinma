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
    private AccountType _selectedAccountType = AccountType.Google;

    public AccountDetailsStepViewModel(SqliteStorage storage)
    {
        _storage = storage;
    }

    partial void OnAccountNameChanged(string value)
    {
        ClearAccountNameValidation();
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

        var isUnique = await _storage.IsAccountNameUniqueAsync(AccountName);
        if (!isUnique)
        {
            SetAccountNameValidationError("An account with this name already exists");
            return false;
        }

        NameValidationError = null;
        AccountNameValidateStatus = FormValidateStatus.Success;
        return true;
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
}
