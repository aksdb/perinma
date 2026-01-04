using System.ComponentModel.DataAnnotations;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using perinma.Storage.Models;

namespace perinma.Views.Settings.AddAccountWizard;

public partial class CalDavConnectionStepViewModel : ObservableValidator
{
    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "Server URL is required")]
    [Url(ErrorMessage = "Please enter a valid URL")]
    private string _serverUrl = string.Empty;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "Username is required")]
    private string _username = string.Empty;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "Password is required")]
    private string _password = string.Empty;

    public bool Validate()
    {
        ValidateAllProperties();
        return !HasErrors;
    }

    public CalDavCredentials GetCredentials()
    {
        // TODO: These credentials will not be stored yet (secret management TBD)
        return new CalDavCredentials
        {
            Type = "CalDav",
            ServerUrl = ServerUrl,
            Username = Username,
            Password = Password
        };
    }
}
