using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using perinma.Storage.Models;

namespace perinma.Views.Settings.AddAccountWizard;

public enum JmapAuthenticationMode
{
    UsernamePassword,
    BearerToken
}

public partial class JmapConnectionStepViewModel : ObservableValidator
{
    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "Session URL is required")]
    [Url(ErrorMessage = "Please enter a valid URL")]
    private string _sessionUrl = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUsernamePasswordMode))]
    [NotifyPropertyChangedFor(nameof(IsBearerTokenMode))]
    private JmapAuthenticationMode _authenticationMode = JmapAuthenticationMode.UsernamePassword;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _bearerToken = string.Empty;

    [ObservableProperty]
    private string? _statusMessage = "Provide your JMAP endpoint and credentials.";

    public bool IsUsernamePasswordMode => AuthenticationMode == JmapAuthenticationMode.UsernamePassword;
    public bool IsBearerTokenMode => AuthenticationMode == JmapAuthenticationMode.BearerToken;

    partial void OnAuthenticationModeChanged(JmapAuthenticationMode value)
    {
        StatusMessage = value == JmapAuthenticationMode.UsernamePassword
            ? "Use your username and password or app password for this JMAP account."
            : "Use a bearer token for this JMAP account.";
    }

    public bool Validate()
    {
        ValidateAllProperties();
        if (HasErrors)
        {
            return false;
        }

        if (IsUsernamePasswordMode)
        {
            return !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);
        }

        return !string.IsNullOrWhiteSpace(BearerToken);
    }

    public JmapCredentials GetCredentials()
    {
        return new JmapCredentials
        {
            Type = "Jmap",
            SessionUrl = SessionUrl,
            Username = IsUsernamePasswordMode ? Username : null,
            Password = IsUsernamePasswordMode ? Password : null,
            BearerToken = IsBearerTokenMode ? BearerToken : null
        };
    }
}
