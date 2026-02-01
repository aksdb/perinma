using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using perinma.Services.CalDAV;
using perinma.Services;
using perinma.Storage;

namespace perinma.Views.Acl;

/// <summary>
/// Represents a principal type option for the dropdown.
/// </summary>
public record PrincipalTypeItem(string Type, string Icon, string DisplayName);

/// <summary>
/// ViewModel for the Add ACE dialog.
/// </summary>
    public partial class AddAceDialogViewModel : ViewModelBase
{
    private readonly Window _ownerWindow;
    private readonly SqliteStorage? _storage;
    private readonly CredentialManagerService? _credentialManager;
    private readonly Models.Calendar? _calendar;

    [ObservableProperty]
    private int _selectedPrincipalTypeIndex = 0;

    [ObservableProperty]
    private string _principalUrl = string.Empty;

    [ObservableProperty]
    private bool _isGrant = true;

    // Principal search properties
    [ObservableProperty]
    private string _searchTerm = string.Empty;

    private ObservableCollection<PrincipalSearchResult> _searchResults = [];
    public ObservableCollection<PrincipalSearchResult> SearchResults => _searchResults;

    [ObservableProperty]
    private bool _isSearching;

    // Privilege checkboxes
    [ObservableProperty]
    private bool _canRead;

    [ObservableProperty]
    private bool _canReadAcl;

    [ObservableProperty]
    private bool _canWrite;

    [ObservableProperty]
    private bool _canWriteProperties;

    [ObservableProperty]
    private bool _canWriteContent;

    [ObservableProperty]
    private bool _canBind;

    [ObservableProperty]
    private bool _canUnbind;

    [ObservableProperty]
    private bool _canUnlock;

    [ObservableProperty]
    private bool _canWriteAcl;

    public ObservableCollection<PrincipalTypeItem> PrincipalTypes { get; }

    /// <summary>
    /// Gets whether to show URL input (for Href principal types).
    /// </summary>
    public bool ShowUrlInput => SelectedPrincipalTypeIndex == 5; // Index of "User or Group URL"

    /// <summary>
    /// Gets whether to show principal search UI.
    /// </summary>
    public bool CanSearchPrincipals => _storage != null && _credentialManager != null && _calendar != null && _calendar.Account?.Type == Models.AccountType.CalDav;

    /// <summary>
    /// Gets whether there are search results to display.
    /// </summary>
    public bool HasSearchResults => _searchResults.Count > 0;

    /// <summary>
    /// Gets whether a principal URL has been entered/selected.
    /// </summary>
    public bool HasPrincipalUrl => !string.IsNullOrEmpty(PrincipalUrl);

    public AddAceDialogViewModel(
        Window ownerWindow,
        SqliteStorage storage,
        CredentialManagerService credentialManager,
        Models.Calendar? calendar,
        string? principalCollectionUrl = null)
    {
        _ownerWindow = ownerWindow;
        _storage = storage;
        _credentialManager = credentialManager;
        _calendar = calendar;
        _principalCollectionUrl = principalCollectionUrl;

        PrincipalTypes = new ObservableCollection<PrincipalTypeItem>
        {
            new PrincipalTypeItem("All", "🌐", "All Users"),
            new PrincipalTypeItem("Authenticated", "👤", "Authenticated Users"),
            new PrincipalTypeItem("Unauthenticated", "👻", "Anonymous Users"),
            new PrincipalTypeItem("Self", "👤", "You (Current User)"),
            new PrincipalTypeItem("Owner", "📋", "Calendar Owner"),
            new PrincipalTypeItem("Href", "🔗", "User or Group URL"),
        };
    }

    private string? _principalCollectionUrl;

    partial void OnSelectedPrincipalTypeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(ShowUrlInput));
    }

        partial void OnSearchTermChanged(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                _searchResults.Clear();
                return;
            }

            // Debounced search - fire and forget, but return the Task
            _ = SearchPrincipalsAsync();
        }

    /// <summary>
    /// Searches for principals using the principal-property-search REPORT.
    /// </summary>
    [RelayCommand]
    private async Task SearchPrincipalsAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchTerm) || _storage == null || _credentialManager == null || _calendar == null)
        {
            _searchResults.Clear();
            return;
        }

        IsSearching = true;

        try
        {
            Console.WriteLine($"Starting principal search for: {SearchTerm}");

            var calDavAccount = _calendar.Account;
            if (calDavAccount == null || calDavAccount.Type != Models.AccountType.CalDav)
            {
                Console.WriteLine($"Calendar is not a CalDAV calendar (Type: {_calendar?.Account?.Type})");
                _searchResults.Clear();
                return;
            }

            Console.WriteLine($"Using CalDAV account: {calDavAccount.Name} ({calDavAccount.Id})");

            var credentials = _credentialManager.GetCalDavCredentials(calDavAccount.Id.ToString());
            if (credentials == null)
            {
                Console.WriteLine("No CalDAV credentials found for principal search");
                _searchResults.Clear();
                return;
            }

            // Use the provided principal collection URL or try to discover it
            var searchUrl = _principalCollectionUrl;
            if (string.IsNullOrEmpty(searchUrl))
            {
                // Try to discover from the account's server URL
                // This is a best effort - some servers may not support principal-collection-set discovery
                Console.WriteLine($"Discovering principal collection URL from: {credentials.ServerUrl}");
                var discoveryClient = new CalDavClient();
                discoveryClient.SetBasicAuth(credentials.Username, credentials.Password);
                var discoveredUrl = await discoveryClient.DiscoverPrincipalCollectionUrlAsync(credentials.ServerUrl);
                Console.WriteLine($"Discovered URL: {discoveredUrl}, will cache and use for search");
                searchUrl = discoveredUrl;
                _principalCollectionUrl = searchUrl; // Cache for future searches
            }

            if (string.IsNullOrEmpty(searchUrl))
            {
                Console.WriteLine("No principal collection URL available for search");
                _searchResults.Clear();
                return;
            }

            Console.WriteLine($"Searching for principals at: {searchUrl}");

            var client = new CalDavClient();
            client.SetBasicAuth(credentials.Username, credentials.Password);
            var results = await client.SearchPrincipalsAsync(searchUrl, SearchTerm);

            Console.WriteLine($"Found {results.Count} principals");

            _searchResults.Clear();
            foreach (var result in results)
            {
                _searchResults.Add(result);
                Console.WriteLine($"  - {result.DisplayName} ({result.Href})");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error searching principals: {ex.Message}");
            _searchResults.Clear();
        }
        finally
        {
            IsSearching = false;
        }
    }

    /// <summary>
    /// Selects a principal from the search results.
    /// </summary>
    public void SelectPrincipal(PrincipalSearchResult principal)
    {
        PrincipalUrl = principal.Href;
        SearchTerm = string.Empty;
        _searchResults.Clear();
    }

    /// <summary>
    /// Clears the principal selection.
    /// </summary>
    [RelayCommand]
    private void ClearPrincipalSelection()
    {
        PrincipalUrl = string.Empty;
        SearchTerm = string.Empty;
        _searchResults.Clear();
    }

    /// <summary>
    /// Sets a preset of privileges.
    /// </summary>
    [RelayCommand]
    private void SetPreset(string preset)
    {
        switch (preset)
        {
            case "Read":
                CanRead = true;
                CanReadAcl = false;
                CanWrite = false;
                CanWriteProperties = false;
                CanWriteContent = false;
                CanBind = false;
                CanUnbind = false;
                CanUnlock = false;
                CanWriteAcl = false;
                break;
            case "ReadWrite":
                CanRead = true;
                CanReadAcl = false;
                CanWrite = true;
                CanWriteProperties = true;
                CanWriteContent = true;
                CanBind = true;
                CanUnbind = true;
                CanUnlock = true;
                CanWriteAcl = false;
                break;
            case "Full":
                CanRead = true;
                CanReadAcl = true;
                CanWrite = true;
                CanWriteProperties = true;
                CanWriteContent = true;
                CanBind = true;
                CanUnbind = true;
                CanUnlock = true;
                CanWriteAcl = true;
                break;
        }
    }

    /// <summary>
    /// Adds the new ACE and closes the dialog.
    /// </summary>
    [RelayCommand]
    private void Add()
    {
        // Create the principal
        WebDavPrincipal principal = SelectedPrincipalTypeIndex switch
        {
            0 => WebDavPrincipal.All(),
            1 => WebDavPrincipal.Authenticated(),
            2 => WebDavPrincipal.Unauthenticated(),
            3 => WebDavPrincipal.FromHref(""), // Will be replaced with actual self URL
            4 => WebDavPrincipal.FromProperty("owner"),
            5 => WebDavPrincipal.FromHref(PrincipalUrl),
            _ => throw new InvalidOperationException("Invalid principal type")
        };

        // Build the privilege list
        var privileges = new List<WebDavPrivilege>();

        if (CanRead)
            privileges.Add(WebDavPrivilege.WebDav.Read);

        if (CanReadAcl)
            privileges.Add(WebDavPrivilege.WebDav.ReadAcl);

        if (CanWrite)
            privileges.Add(WebDavPrivilege.WebDav.Write);

        if (CanWriteProperties)
            privileges.Add(WebDavPrivilege.WebDav.WriteProperties);

        if (CanWriteContent)
            privileges.Add(WebDavPrivilege.WebDav.WriteContent);

        if (CanBind)
            privileges.Add(WebDavPrivilege.WebDav.Bind);

        if (CanUnbind)
            privileges.Add(WebDavPrivilege.WebDav.Unbind);

        if (CanUnlock)
            privileges.Add(WebDavPrivilege.WebDav.Unlock);

        if (CanWriteAcl)
            privileges.Add(WebDavPrivilege.WebDav.WriteAcl);

        // Create the ACE
        var ace = new WebDavAce(
            principal,
            false, // not inverted
            IsGrant,
            false, // not protected
            null, // not inherited
            privileges.ToArray()
        );

        _ownerWindow.Close(ace);
    }

    /// <summary>
    /// Cancels and closes the dialog.
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        _ownerWindow.Close(null);
    }
}
