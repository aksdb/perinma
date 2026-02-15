using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using perinma.Models;
using perinma.Storage;
using perinma.Storage.Models;

namespace perinma.Views.Calendar.EventView;

/// <summary>
/// ViewModel for a calendar event participant with contact enrichment and photo loading.
/// </summary>
public partial class ParticipantViewModel(CalendarEventParticipant participant) : ViewModelBase
{
    [ObservableProperty]
    private string _displayName = participant.Name ?? participant.Email;

    [ObservableProperty]
    private string _email = participant.Email;

    [ObservableProperty]
    private EventResponseStatus _status = participant.Status;

    [ObservableProperty]
    private bool _isOrganizer = participant.IsOrganizer;

    [ObservableProperty]
    private bool _hasContact;

    [ObservableProperty]
    private string? _photoUrl;

    [ObservableProperty]
    private Bitmap? _photoBitmap;

    [ObservableProperty]
    private string _initials = GetInitials(participant.Name, participant.Email);

    /// <summary>
    /// Gets whether a photo is available for display.
    /// </summary>
    public bool HasPhoto => PhotoBitmap != null;

    /// <summary>
    /// Gets whether this participant has a response status.
    /// </summary>
    public bool HasStatus => Status != EventResponseStatus.None;

    /// <summary>
    /// Gets the icon character representing the response status.
    /// </summary>
    public string StatusIcon => Status switch
    {
        EventResponseStatus.Accepted => "\u2713",
        EventResponseStatus.Declined => "\u2717",
        EventResponseStatus.Tentative => "?",
        EventResponseStatus.NeedsAction => "\u2022",
        _ => ""
    };

    /// <summary>
    /// Gets the tooltip text for the response status.
    /// </summary>
    public string StatusTooltip => Status switch
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
    public string StatusColor => Status switch
    {
        EventResponseStatus.Accepted => "#10B981",
        EventResponseStatus.Declined => "#EF4444",
        EventResponseStatus.Tentative => "#F59E0B",
        EventResponseStatus.NeedsAction => "#6B7280",
        _ => "#6B7280"
    };

    /// <summary>
    /// Asynchronously loads contact information and enriches participant.
    /// </summary>
    public async Task EnrichFromContactsAsync(CancellationToken cancellationToken = default)
    {
        if (App.Services == null)
        {
            return;
        }

        try
        {
            var storage = App.Services.GetRequiredService<SqliteStorage>();
            var contact = await storage.GetContactByEmailAsync(Email);
            if (contact != null)
            {
                HasContact = true;

                if (!string.IsNullOrWhiteSpace(contact.DisplayName))
                {
                    DisplayName = contact.DisplayName;
                }
                else if (!string.IsNullOrWhiteSpace(contact.GivenName) || !string.IsNullOrWhiteSpace(contact.FamilyName))
                {
                    DisplayName = $"{contact.GivenName} {contact.FamilyName}".Trim();
                }

                Initials = GetInitials(contact.GivenName, contact.FamilyName, DisplayName);
                PhotoUrl = contact.PhotoUrl;

                _ = LoadPhotoAsync(cancellationToken);
            }
        }
        catch
        {
        }
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

    private static string GetInitials(string? name, string email)
    {
        var nameToUse = string.IsNullOrWhiteSpace(name) ? email : name;

        if (string.IsNullOrWhiteSpace(nameToUse))
            return "?";

        if (nameToUse.Contains('@'))
        {
            var localPart = nameToUse.Split('@')[0];
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

        var nameParts = nameToUse.Split(' ', StringSplitOptions.RemoveEmptyEntries);
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

    private static string GetInitials(string? givenName, string? familyName, string? displayName)
    {
        if (!string.IsNullOrWhiteSpace(givenName) && !string.IsNullOrWhiteSpace(familyName))
        {
            return $"{givenName[0]}{familyName[0]}".ToUpperInvariant();
        }

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return GetInitials(displayName, displayName);
        }

        return "?";
    }
}
