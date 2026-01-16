using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using perinma.Services;
using perinma.Storage.Models;

namespace perinma.Views.Settings.AddAccountWizard;

public partial class CalDavConnectionStepViewModel : ObservableValidator
{
    private readonly ICalDavService _calDavService;

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

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isConnectionSuccessful;

    public CalDavConnectionStepViewModel(ICalDavService calDavService)
    {
        _calDavService = calDavService;
    }

    public bool Validate()
    {
        ValidateAllProperties();
        return !HasErrors;
    }

    public CalDavCredentials GetCredentials()
    {
        return new CalDavCredentials
        {
            Type = "CalDAV",
            ServerUrl = ServerUrl,
            Username = Username,
            Password = Password
        };
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (!Validate())
        {
            StatusMessage = "Please fix validation errors before testing connection.";
            IsConnectionSuccessful = false;
            return;
        }

        var credentials = GetCredentials();

        try
        {
            IsLoading = true;
            StatusMessage = "Testing connection...";
            IsConnectionSuccessful = false;

            var success = await _calDavService.TestConnectionAsync(credentials);

            if (success)
            {
                StatusMessage = "Connection successful!";
                IsConnectionSuccessful = true;
            }
            else
            {
                StatusMessage = "Connection failed. Please check your server URL and credentials.";
                IsConnectionSuccessful = false;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            IsConnectionSuccessful = false;
        }
        finally
        {
            IsLoading = false;
        }
    }
}
