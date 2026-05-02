using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using perinma.Models;
using perinma.Storage;
using perinma.Storage.Models;

namespace perinma.Views.Calendar.EventEdit;

public partial class ParticipantEditItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private bool _isOptional;

    [ObservableProperty]
    private bool _hasContact;

    [ObservableProperty]
    private string? _photoUrl;

    [ObservableProperty]
    private Bitmap? _photoBitmap;

    [ObservableProperty]
    private string _initials = "?";

    public bool HasPhoto => PhotoBitmap != null;

    public Action? RemoveAction { get; set; }

    public ParticipantEditItemViewModel(string email, string? displayName = null)
    {
        Email = email;
        DisplayName = displayName ?? email;
        Initials = GenerateInitials(null, null, displayName);
        HasContact = false;
    }

    public ParticipantEditItemViewModel(ContactQueryResult contact)
    {
        Email = contact.PrimaryEmail ?? string.Empty;
        DisplayName = contact.DisplayName ??
                     $"{contact.GivenName} {contact.FamilyName}".Trim() ??
                     Email;
        Initials = GenerateInitials(contact.GivenName, contact.FamilyName, contact.DisplayName);
        HasContact = true;
        PhotoUrl = contact.PhotoUrl;

        _ = LoadPhotoAsync();
    }

    [RelayCommand]
    private void Remove()
    {
        RemoveAction?.Invoke();
    }

    public async Task LoadPhotoAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(PhotoUrl))
        {
            PhotoBitmap = null;
            OnPropertyChanged(nameof(HasPhoto));
            return;
        }

        try
        {
            if (PhotoUrl.StartsWith("blob://", StringComparison.OrdinalIgnoreCase))
            {
                var base64Data = PhotoUrl["blob://".Length..];
                var queryIndex = base64Data.IndexOf('?');
                if (queryIndex >= 0)
                {
                    base64Data = base64Data[..queryIndex];
                }
                var bytes = Convert.FromBase64String(base64Data);
                using var stream = new MemoryStream(bytes);
                PhotoBitmap = new Bitmap(stream);
                OnPropertyChanged(nameof(HasPhoto));
                return;
            }

            if (PhotoUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                using var httpClient = new HttpClient();
                using var response = await httpClient.GetAsync(PhotoUrl, cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    PhotoBitmap = new Bitmap(stream);
                }
                else
                {
                    PhotoBitmap = null;
                }
                OnPropertyChanged(nameof(HasPhoto));
                return;
            }

            PhotoBitmap = null;
            OnPropertyChanged(nameof(HasPhoto));
        }
        catch
        {
            PhotoBitmap = null;
            OnPropertyChanged(nameof(HasPhoto));
        }
    }

    private static string GenerateInitials(string? givenName, string? familyName, string? displayName)
    {
        if (!string.IsNullOrWhiteSpace(givenName) && !string.IsNullOrWhiteSpace(familyName))
        {
            return $"{givenName[0]}{familyName[0]}".ToUpperInvariant();
        }

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return GenerateInitialsFromName(displayName);
        }

        return "?";
    }

    private static string GenerateInitialsFromName(string nameOrEmail)
    {
        if (string.IsNullOrWhiteSpace(nameOrEmail))
            return "?";

        if (nameOrEmail.Contains('@'))
        {
            var localPart = nameOrEmail.Split('@')[0];
            var parts = localPart.Split(['.', '_', '-'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                return $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant();
            }
            if (parts.Length == 1 && parts[0].Length >= 2)
            {
                return parts[0][..2].ToUpperInvariant();
            }
            return parts[0][0].ToString().ToUpperInvariant();
        }

        var nameParts = nameOrEmail.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (nameParts.Length >= 2)
        {
            return $"{nameParts[0][0]}{nameParts[^1][0]}".ToUpperInvariant();
        }
        if (nameParts.Length == 1 && nameParts[0].Length >= 1)
        {
            return nameParts[0][0].ToString().ToUpperInvariant();
        }

        return "?";
    }
}
