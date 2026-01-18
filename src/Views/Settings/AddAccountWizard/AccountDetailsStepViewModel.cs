using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
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
    private AccountType _selectedAccountType = AccountType.Google;

    public AccountDetailsStepViewModel(SqliteStorage storage)
    {
        _storage = storage;
    }

    public async Task<bool> ValidateAsync()
    {
        // Validate basic property rules
        ValidateAllProperties();
        if (HasErrors)
        {
            NameValidationError = GetErrors(nameof(AccountName))
                .Cast<ValidationResult>()
                .FirstOrDefault()?.ErrorMessage;
            return false;
        }

        // Check name uniqueness in database
        var isUnique = await _storage.IsAccountNameUniqueAsync(AccountName);
        if (!isUnique)
        {
            NameValidationError = "An account with this name already exists";
            return false;
        }

        NameValidationError = null;
        return true;
    }
}
