using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using perinma.Models;
using perinma.Storage.Models;

namespace perinma.Views.Calendar;

/// <summary>
/// ViewModel for an event attendee with optional contact enrichment and photo loading.
/// </summary>
public partial class AttendeeViewModel : ObservableObject
{
    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private EventResponseStatus _responseStatus = EventResponseStatus.None;

    [ObservableProperty]
    private bool _isOrganizer;

    [ObservableProperty]
    private bool _hasContact;

    [ObservableProperty]
    private string? _photoUrl;

    [ObservableProperty]
    private Bitmap? _photoBitmap;

    [ObservableProperty]
    private string _initials = "?";

    /// <summary>
    /// Gets whether a photo is available for display
    /// </summary>
    public bool HasPhoto => PhotoBitmap != null;

    /// <summary>
    /// Gets the icon character representing the response status.
    /// </summary>
    public string StatusIcon => ResponseStatus switch
    {
        EventResponseStatus.Accepted => "\u2713",    // ✓ checkmark
        EventResponseStatus.Declined => "\u2717",    // ✗ cross
        EventResponseStatus.Tentative => "?",        // question mark
        EventResponseStatus.NeedsAction => "\u2022", // • bullet
        _ => ""
    };

    /// <summary>
    /// Gets the tooltip text for the response status.
    /// </summary>
    public string StatusTooltip => ResponseStatus switch
    {
        EventResponseStatus.Accepted => "Accepted",
        EventResponseStatus.Declined => "Declined",
        EventResponseStatus.Tentative => "Tentative",
        EventResponseStatus.NeedsAction => "Not responded",
        _ => "Unknown"
    };

    /// <summary>
    /// Gets the color for the status icon.
    /// </summary>
    public string StatusColor => ResponseStatus switch
    {
        EventResponseStatus.Accepted => "#10B981",   // emerald
        EventResponseStatus.Declined => "#EF4444",   // red
        EventResponseStatus.Tentative => "#F59E0B",  // amber
        EventResponseStatus.NeedsAction => "#6B7280", // gray
        _ => "#6B7280"
    };

    /// <summary>
    /// Creates an AttendeeViewModel from basic attendee info.
    /// </summary>
    public static AttendeeViewModel Create(
        string nameOrEmail,
        string? email,
        EventResponseStatus responseStatus,
        bool isOrganizer)
    {
        var vm = new AttendeeViewModel
        {
            DisplayName = nameOrEmail,
            Email = email ?? nameOrEmail,
            ResponseStatus = responseStatus,
            IsOrganizer = isOrganizer,
            HasContact = false
        };

        vm.Initials = GenerateInitials(nameOrEmail);
        return vm;
    }

    /// <summary>
    /// Enriches this attendee with contact information if a matching contact is found.
    /// </summary>
    public void EnrichWithContact(ContactQueryResult contact)
    {
        HasContact = true;

        // Use contact's display name if available
        if (!string.IsNullOrWhiteSpace(contact.DisplayName))
        {
            DisplayName = contact.DisplayName;
        }
        else if (!string.IsNullOrWhiteSpace(contact.GivenName) || !string.IsNullOrWhiteSpace(contact.FamilyName))
        {
            DisplayName = $"{contact.GivenName} {contact.FamilyName}".Trim();
        }

        // Update initials based on contact name
        Initials = GenerateInitials(contact.GivenName, contact.FamilyName, DisplayName);

        // Set photo URL for later loading
        PhotoUrl = contact.PhotoUrl;
    }

    /// <summary>
    /// Loads the photo asynchronously from the PhotoUrl.
    /// </summary>
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
            // Handle blob:// URLs (embedded base64 data)
            if (PhotoUrl.StartsWith("blob://", StringComparison.OrdinalIgnoreCase))
            {
                var base64Data = PhotoUrl["blob://".Length..];
                // Strip query parameters if present
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

    private static string GenerateInitials(string nameOrEmail)
    {
        if (string.IsNullOrWhiteSpace(nameOrEmail))
            return "?";

        // If it looks like an email, extract initials from the local part
        if (nameOrEmail.Contains('@'))
        {
            var localPart = nameOrEmail.Split('@')[0];
            // Try to split by dots or underscores
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

        // Regular name - split by spaces
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

    private static string GenerateInitials(string? givenName, string? familyName, string? displayName)
    {
        if (!string.IsNullOrWhiteSpace(givenName) && !string.IsNullOrWhiteSpace(familyName))
        {
            return $"{givenName[0]}{familyName[0]}".ToUpperInvariant();
        }

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return GenerateInitials(displayName);
        }

        return "?";
    }
}
