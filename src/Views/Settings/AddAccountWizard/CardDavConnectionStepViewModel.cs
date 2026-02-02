using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using perinma.Services.CardDAV;
using perinma.Storage.Models;

namespace perinma.Views.Settings.AddAccountWizard;

public partial class CardDavConnectionStepViewModel : ObservableValidator
{
    private readonly ICardDavService _cardDavService;

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

    public CardDavConnectionStepViewModel(ICardDavService cardDavService)
    {
        _cardDavService = cardDavService;
    }

    public bool Validate()
    {
        ValidateAllProperties();
        return !HasErrors;
    }

    public CardDavCredentials GetCredentials()
    {
        return new CardDavCredentials
        {
            Type = "CardDAV",
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

            var success = await _cardDavService.TestConnectionAsync(credentials);

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
