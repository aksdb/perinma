using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
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
        
        // Parse extended fields from RawData
        ParseExtendedFields(contact.RawData, contact.AccountTypeEnum);
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
    /// All email addresses for this contact
    /// </summary>
    public List<ContactEmailField> Emails { get; } = [];

    /// <summary>
    /// All phone numbers for this contact
    /// </summary>
    public List<ContactPhoneField> Phones { get; } = [];

    /// <summary>
    /// All postal addresses for this contact
    /// </summary>
    public List<ContactAddressField> Addresses { get; } = [];

    /// <summary>
    /// Gets whether this contact has any email addresses
    /// </summary>
    public bool HasEmails => Emails.Count > 0;

    /// <summary>
    /// Gets whether this contact has any phone numbers
    /// </summary>
    public bool HasPhones => Phones.Count > 0;

    /// <summary>
    /// Gets whether this contact has any postal addresses
    /// </summary>
    public bool HasAddresses => Addresses.Count > 0;

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

    /// <summary>
    /// Parses extended contact fields from RawData JSON
    /// </summary>
    private void ParseExtendedFields(string? rawData, AccountType accountType)
    {
        if (string.IsNullOrWhiteSpace(rawData))
            return;

        try
        {
            if (accountType == AccountType.Google)
            {
                ParseGooglePersonFields(rawData);
            }
            else if (accountType == AccountType.CardDav)
            {
                ParseVCardFields(rawData);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to parse extended contact fields: {ex.Message}");
        }
    }

    /// <summary>
    /// Parses Google Person JSON to extract extended fields
    /// </summary>
    private void ParseGooglePersonFields(string rawData)
    {
        using var doc = JsonDocument.Parse(rawData);
        var root = doc.RootElement;

        // Parse email addresses
        if (root.TryGetProperty("emailAddresses", out var emailsElement))
        {
            foreach (var email in emailsElement.EnumerateArray())
            {
                var value = email.TryGetProperty("value", out var v) ? v.GetString() : null;
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                var label = email.TryGetProperty("type", out var t) ? t.GetString() : null;
                var isPrimary = email.TryGetProperty("metadata", out var meta)
                                && meta.TryGetProperty("primary", out var primary)
                                && primary.GetBoolean();

                Emails.Add(new ContactEmailField(value, label, isPrimary));
            }
        }

        // Parse phone numbers
        if (root.TryGetProperty("phoneNumbers", out var phonesElement))
        {
            foreach (var phone in phonesElement.EnumerateArray())
            {
                var value = phone.TryGetProperty("value", out var v) ? v.GetString() : null;
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                var label = phone.TryGetProperty("type", out var t) ? t.GetString() : null;
                var isPrimary = phone.TryGetProperty("metadata", out var meta)
                                && meta.TryGetProperty("primary", out var primary)
                                && primary.GetBoolean();

                Phones.Add(new ContactPhoneField(value, label, isPrimary));
            }
        }

        // Parse addresses
        if (root.TryGetProperty("addresses", out var addressesElement))
        {
            foreach (var addr in addressesElement.EnumerateArray())
            {
                var formattedValue = addr.TryGetProperty("formattedValue", out var fv) ? fv.GetString() : null;
                var streetAddress = addr.TryGetProperty("streetAddress", out var sa) ? sa.GetString() : null;
                var city = addr.TryGetProperty("city", out var c) ? c.GetString() : null;
                var region = addr.TryGetProperty("region", out var r) ? r.GetString() : null;
                var postalCode = addr.TryGetProperty("postalCode", out var pc) ? pc.GetString() : null;
                var country = addr.TryGetProperty("country", out var co) ? co.GetString() : null;
                var label = addr.TryGetProperty("type", out var t) ? t.GetString() : null;
                var isPrimary = addr.TryGetProperty("metadata", out var meta)
                                && meta.TryGetProperty("primary", out var primary)
                                && primary.GetBoolean();

                // Only add if there's some address data
                if (!string.IsNullOrWhiteSpace(formattedValue) ||
                    !string.IsNullOrWhiteSpace(streetAddress) ||
                    !string.IsNullOrWhiteSpace(city))
                {
                    Addresses.Add(new ContactAddressField(
                        formattedValue, streetAddress, city, region, postalCode, country, label, isPrimary));
                }
            }
        }

        // Sort: primary first, then by label
        SortFields();
    }

    /// <summary>
    /// Parses vCard format to extract extended fields (for CardDAV contacts)
    /// </summary>
    private void ParseVCardFields(string rawData)
    {
        // vCard format parsing - each line is PROPERTY:VALUE or PROPERTY;PARAMS:VALUE
        var lines = rawData.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var colonIndex = line.IndexOf(':');
            if (colonIndex < 0)
                continue;

            var propertyPart = line[..colonIndex];
            var value = line[(colonIndex + 1)..];

            if (string.IsNullOrWhiteSpace(value))
                continue;

            // Parse property name and parameters
            var parts = propertyPart.Split(';');
            var propertyName = parts[0].ToUpperInvariant();
            var label = ExtractVCardTypeParam(parts);
            var isPref = parts.Any(p => p.Equals("PREF", StringComparison.OrdinalIgnoreCase));

            switch (propertyName)
            {
                case "EMAIL":
                    Emails.Add(new ContactEmailField(value, label, isPref));
                    break;

                case "TEL":
                    Phones.Add(new ContactPhoneField(value, label, isPref));
                    break;

                case "ADR":
                    // ADR format: PO Box;Extended;Street;City;Region;Postal;Country
                    var addrParts = value.Split(';');
                    var street = addrParts.Length > 2 ? addrParts[2] : null;
                    var city = addrParts.Length > 3 ? addrParts[3] : null;
                    var region = addrParts.Length > 4 ? addrParts[4] : null;
                    var postal = addrParts.Length > 5 ? addrParts[5] : null;
                    var country = addrParts.Length > 6 ? addrParts[6] : null;

                    if (!string.IsNullOrWhiteSpace(street) || !string.IsNullOrWhiteSpace(city))
                    {
                        Addresses.Add(new ContactAddressField(
                            null, street, city, region, postal, country, label, isPref));
                    }
                    break;
            }
        }

        SortFields();
    }

    /// <summary>
    /// Extracts the TYPE parameter from vCard property parameters
    /// </summary>
    private static string? ExtractVCardTypeParam(string[] parts)
    {
        foreach (var part in parts.Skip(1))
        {
            if (part.StartsWith("TYPE=", StringComparison.OrdinalIgnoreCase))
            {
                return part[5..];
            }
            // Some vCards use just the type without TYPE= prefix
            if (part.Equals("HOME", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("WORK", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("CELL", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("MOBILE", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("FAX", StringComparison.OrdinalIgnoreCase))
            {
                return part;
            }
        }
        return null;
    }

    /// <summary>
    /// Sorts extended fields with primary items first
    /// </summary>
    private void SortFields()
    {
        // Sort emails: primary first
        var sortedEmails = Emails.OrderByDescending(e => e.IsPrimary).ToList();
        Emails.Clear();
        Emails.AddRange(sortedEmails);

        // Sort phones: primary first
        var sortedPhones = Phones.OrderByDescending(p => p.IsPrimary).ToList();
        Phones.Clear();
        Phones.AddRange(sortedPhones);

        // Sort addresses: primary first
        var sortedAddresses = Addresses.OrderByDescending(a => a.IsPrimary).ToList();
        Addresses.Clear();
        Addresses.AddRange(sortedAddresses);
    }
}
