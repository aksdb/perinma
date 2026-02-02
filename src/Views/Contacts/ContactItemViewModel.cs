using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using perinma.Models;
using perinma.Storage.Models;

namespace perinma.Views.Contacts;

public partial class ContactItemViewModel : ObservableObject
{
    public ContactItemViewModel(ContactQueryResult contact)
    {
        ContactId = Guid.Parse(contact.ContactId);
        AddressBookId = Guid.Parse(contact.AddressBookId);
        ExternalId = contact.ExternalId;
        DisplayName = contact.DisplayName ?? string.Empty;
        GivenName = contact.GivenName;
        FamilyName = contact.FamilyName;
        PrimaryEmail = contact.PrimaryEmail;
        PrimaryPhone = contact.PrimaryPhone;
        PhotoUrl = contact.PhotoUrl;
        AddressBookName = contact.AddressBookName;
        AccountName = contact.AccountName;
        AccountType = contact.AccountTypeEnum;
        RawData = contact.RawData;
        
        // Generate initials for avatar
        Initials = GenerateInitials(contact.GivenName, contact.FamilyName, contact.DisplayName);
    }

    [ObservableProperty]
    private Guid _contactId;

    [ObservableProperty]
    private Guid _addressBookId;

    [ObservableProperty]
    private string? _externalId;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string? _givenName;

    [ObservableProperty]
    private string? _familyName;

    [ObservableProperty]
    private string? _primaryEmail;

    [ObservableProperty]
    private string? _primaryPhone;

    [ObservableProperty]
    private string? _photoUrl;

    [ObservableProperty]
    private string _initials = string.Empty;

    [ObservableProperty]
    private string _addressBookName = string.Empty;

    [ObservableProperty]
    private string _accountName = string.Empty;

    [ObservableProperty]
    private AccountType _accountType;

    [ObservableProperty]
    private string? _rawData;

    [ObservableProperty]
    private Bitmap? _photoBitmap;

    /// <summary>
    /// Gets whether a photo is available for display
    /// </summary>
    public bool HasPhoto => PhotoBitmap != null;

    /// <summary>
    /// Gets the subtitle to display (email or phone)
    /// </summary>
    public string? Subtitle => PrimaryEmail ?? PrimaryPhone;

    private static string GenerateInitials(string? givenName, string? familyName, string? displayName)
    {
        if (!string.IsNullOrWhiteSpace(givenName) && !string.IsNullOrWhiteSpace(familyName))
        {
            return $"{givenName[0]}{familyName[0]}".ToUpperInvariant();
        }

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            var parts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                return $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant();
            }
            if (parts.Length == 1 && parts[0].Length >= 1)
            {
                return parts[0][0].ToString().ToUpperInvariant();
            }
        }

        return "?";
    }

    /// <summary>
    /// Loads the contact photo asynchronously from the PhotoUrl
    /// </summary>
    public async Task LoadPhotoAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(PhotoUrl))
        {
            PhotoBitmap = null;
            return;
        }

        try
        {
            // Handle blob:// URLs (embedded base64 data)
            if (PhotoUrl.StartsWith("blob://", StringComparison.OrdinalIgnoreCase))
            {
                var base64Data = PhotoUrl["blob://".Length..];
                var bytes = Convert.FromBase64String(base64Data);
                using var stream = new MemoryStream(bytes);
                PhotoBitmap = new Bitmap(stream);
                return;
            }

            // Handle https:// URLs (download from network)
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
                return;
            }

            // Unknown protocol
            PhotoBitmap = null;
        }
        catch
        {
            // Silently fail on photo load errors - will show initials instead
            PhotoBitmap = null;
        }
    }
}
