using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using perinma.Services;
using perinma.Services.CalDAV;
using perinma.Storage;
using perinma.Views.MessageBox;

namespace perinma.Views.Acl;

/// <summary>
/// ViewModel for managing CalDAV ACL (Access Control List) permissions.
/// </summary>
public partial class AclManagementViewModel : ViewModelBase
{
    private readonly Models.Calendar _calendar;
    private readonly SqliteStorage _storage;
    private readonly CredentialManagerService _credentialManager;
    private readonly Window _ownerWindow;

    [ObservableProperty]
    private string _calendarName = string.Empty;

    [ObservableProperty]
    private string? _calendarColor;

    [ObservableProperty]
    private string _ownerText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasChanges;

    [ObservableProperty]
    private bool _wasSaved;

    /// <summary>
    /// Gets the list of access control entries (ACEs).
    /// </summary>
    public ObservableCollection<AceItemViewModel> Aces { get; } = [];

    /// <summary>
    /// Event raised when the window should close.
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// Gets or sets the currently selected ACE.
    /// </summary>
    [ObservableProperty]
    private AceItemViewModel? _selectedAce;

    // Cache of current user privileges
    private WebDavCurrentUserPrivilegeSet? _currentUserPrivilegeSet;

    public AclManagementViewModel()
    {
        // Design-time constructor
        _calendar = null!;
        _storage = null!;
        _credentialManager = null!;
        _ownerWindow = null!;
    }

    public AclManagementViewModel(
        Window ownerWindow,
        Models.Calendar calendar,
        string? ownerUrl,
        string? aclXml,
        string? currentUserPrivilegeSetXml,
        SqliteStorage storage,
        CredentialManagerService credentialManager)
    {
        _ownerWindow = ownerWindow;
        _calendar = calendar;
        _storage = storage;
        _credentialManager = credentialManager;
        _calendarName = calendar.Name;
        _calendarColor = calendar.Color;

        OwnerText = string.IsNullOrEmpty(ownerUrl) ? "No owner information" : $"Owner: {ownerUrl}";

        // Parse current user privileges if provided
        if (!string.IsNullOrEmpty(currentUserPrivilegeSetXml))
        {
            var privilegeElement = System.Xml.Linq.XElement.Parse(currentUserPrivilegeSetXml);
            _currentUserPrivilegeSet = WebDavAclParser.ParseCurrentUserPrivilegeSet(privilegeElement);
        }

        // Load ACL data
        _ = LoadAclDataAsync(aclXml);
    }

    /// <summary>
    /// Gets whether the current user can read the calendar.
    /// </summary>
    public bool CanRead => _currentUserPrivilegeSet?.CanRead ?? false;

    /// <summary>
    /// Gets whether the current user can write to the calendar.
    /// </summary>
    public bool CanWrite => _currentUserPrivilegeSet?.CanWrite ?? false;

    /// <summary>
    /// Gets whether the current user can modify ACLs.
    /// </summary>
    public bool CanWriteAcl => _currentUserPrivilegeSet?.CanWriteAcl ?? false;

    /// <summary>
    /// Gets whether the current user does not have write-ACL privilege.
    /// </summary>
    public bool NotHasWriteAcl => !CanWriteAcl;

    /// <summary>
    /// Gets whether to show current user privileges.
    /// </summary>
    public bool ShowCurrentUserPrivileges => _currentUserPrivilegeSet != null;

    /// <summary>
    /// Loads ACL data from XML or fetches it from the server.
    /// </summary>
    private async Task LoadAclDataAsync(string? aclXml)
    {
        IsLoading = true;

        try
        {
            // If ACL XML is not provided, fetch it from the server
            if (string.IsNullOrEmpty(aclXml))
            {
                aclXml = await FetchAclFromServerAsync();
            }

            ParseAclXml(aclXml);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading ACL data: {ex.Message}");
            await MessageBoxWindow.ShowAsync(
                _ownerWindow,
                "Error",
                $"Failed to load permissions: {ex.Message}",
                MessageBoxType.Error,
                MessageBoxButtons.Ok);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Fetches ACL data from the CalDAV server.
    /// </summary>
    private async Task<string> FetchAclFromServerAsync()
    {
        // TODO this should be done in the provider
        
        // Get the account for this calendar
        // This is a simplified approach - in production, you'd need to determine which account owns this calendar
        var accounts = await _storage.GetAllAccountsAsync();
        var calDavAccount = accounts.FirstOrDefault(a => a.AccountTypeEnum == Models.AccountType.CalDav);

        if (calDavAccount == null)
        {
            throw new InvalidOperationException("No CalDAV account found for this calendar");
        }

        var credentials = _credentialManager.GetCalDavCredentials(calDavAccount.AccountId);
        if (credentials == null)
        {
            throw new InvalidOperationException("No CalDAV credentials found");
        }

        var client = new CalDavClient();
        client.SetBasicAuth(credentials.Username, credentials.Password);
        return await client.GetAclAsync(_calendar.ExternalId!);
    }

    /// <summary>
    /// Parses ACL XML and populates the ACE list.
    /// </summary>
    private void ParseAclXml(string aclXml)
    {
        var aclElement = System.Xml.Linq.XElement.Parse(aclXml);
        var acl = WebDavAclParser.ParseAcl(aclElement);

        Aces.Clear();

        foreach (var ace in acl.Aces)
        {
            var aceViewModel = new AceItemViewModel(ace, _ownerWindow, this);
            aceViewModel.PropertyChanged += (_, _) => HasChanges = true;
            Aces.Add(aceViewModel);
        }
    }

    /// <summary>
    /// Adds a new ACE to the list.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task AddAceAsync()
    {
        if (!CanEdit)
            return;

        // Show the dialog with services for principal search
        var ace = await AddAceDialog.ShowAsync(
            _ownerWindow,
            _storage,
            _credentialManager,
            _calendar);

        if (ace != null)
        {
            // Create AceItemViewModel with parent reference
            var result = new AceItemViewModel(ace, _ownerWindow, this);
            Aces.Add(result);
            HasChanges = true;
        }
    }

    /// <summary>
    /// Removes the selected ACE from the list.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void RemoveSelectedAce()
    {
        if (!CanEdit || SelectedAce == null)
            return;

        RemoveAce(SelectedAce);
    }

    /// <summary>
    /// Removes a specific ACE from the list.
    /// </summary>
    /// <param name="ace">The ACE to remove</param>
    public async void RemoveAce(AceItemViewModel ace)
    {
        if (!CanEdit || ace == null)
            return;

        if (ace.IsProtected || ace.IsInherited)
        {
            _ = MessageBoxWindow.ShowAsync(
                _ownerWindow,
                "Cannot Remove",
                "This permission is protected or inherited and cannot be removed.",
                MessageBoxType.Warning,
                MessageBoxButtons.Ok);
            return;
        }

        var result = await MessageBoxWindow.ShowAsync(
            _ownerWindow,
            "Remove Permission",
            $"Are you sure you want to remove the permission for '{ace.PrincipalDisplayText}'?",
            MessageBoxType.Confirmation,
            MessageBoxButtons.YesNo);

        if (result == MessageBoxResult.Yes)
        {
            Aces.Remove(ace);
            if (SelectedAce == ace)
            {
                SelectedAce = null;
            }
            HasChanges = true;
        }
    }

    /// <summary>
    /// Determines if the current user can edit ACLs.
    /// </summary>
    private bool CanEdit => !IsLoading && CanWriteAcl;

    /// <summary>
    /// Saves the changes to the CalDAV server.
    /// </summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!HasChanges)
            return;

        try
        {
            IsLoading = true;
            // TODO this should be done in the provider

            // Build ACL from modified ACE list
            var acl = new WebDavAcl(Aces.Select(ace => ace.ToWebDavAce()).ToArray());
            var aclXml = WebDavAclParser.BuildAclXml(acl);
            Console.WriteLine($"Built ACL XML (length: {aclXml?.Length ?? 0})");

            // Get credentials and save to server
            var credentials = _credentialManager.GetCalDavCredentials(_calendar.Account.Id.ToString());
            if (credentials == null)
            {
                throw new InvalidOperationException("No CalDAV credentials found");
            }

            var client = new CalDavClient();
            client.SetBasicAuth(credentials.Username, credentials.Password);
            await client.SetAclAsync(_calendar.ExternalId!, acl);

            // Update local database with new ACL data
            await _storage.SetCalendarDataAsync(_calendar.Id.ToString(), "rawACL", aclXml);
            Console.WriteLine($"Updated ACL in database for calendar {_calendar.Name}");

            WasSaved = true;
            await MessageBoxWindow.ShowAsync(
                _ownerWindow,
                "Success",
                "Permissions have been saved successfully.",
                MessageBoxType.Information,
                MessageBoxButtons.Ok);

            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving ACL: {ex.Message}");
            await MessageBoxWindow.ShowAsync(
                _ownerWindow,
                "Error",
                $"Failed to save permissions: {ex.Message}",
                MessageBoxType.Error,
                MessageBoxButtons.Ok);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Cancels the changes and closes the window.
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        if (HasChanges)
        {
            // In a real implementation, you might want to show a confirmation dialog
            // For now, we'll just close
        }

        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// ViewModel for a single Access Control Entry (ACE).
/// </summary>
public partial class AceItemViewModel : ViewModelBase
{
    private readonly WebDavAce _originalAce;
    private readonly Window _ownerWindow;

    [ObservableProperty]
    private bool _isSelected;

    public WebDavPrincipal? Principal => _originalAce.Principal;

    public bool IsGrant => _originalAce.IsGrant;

    public bool IsProtected => _originalAce.IsProtected;

    public bool IsInherited => !string.IsNullOrEmpty(_originalAce.InheritedFrom);

    public ObservableCollection<WebDavPrivilege> Privileges { get; }

    public string GrantDenyText => IsGrant ? "Grant" : "Deny";

    public string PrincipalDisplayText => GetPrincipalDisplayText();

    public string PrincipalTypeIcon => GetPrincipalTypeIcon();

    public string PrincipalTypeColor => GetPrincipalTypeColor();

    public AceItemViewModel(WebDavAce ace, Window ownerWindow, AclManagementViewModel? parentViewModel = null)
    {
        _originalAce = ace;
        _ownerWindow = ownerWindow;
        _parentViewModel = parentViewModel;
        Privileges = new ObservableCollection<WebDavPrivilege>(ace.Privileges);
    }

    private readonly AclManagementViewModel? _parentViewModel;

    private string GetPrincipalDisplayText()
    {
        if (Principal == null)
            return "Unknown";

        if (Principal.IsAll)
            return "All Users";
        if (Principal.IsAuthenticated)
            return "Authenticated Users";
        if (Principal.IsUnauthenticated)
            return "Anonymous Users";
        if (Principal.IsSelf)
            return "You (Current User)";
        if (!string.IsNullOrEmpty(Principal.PropertyPrincipal))
            return Principal.PropertyPrincipal;
        if (!string.IsNullOrEmpty(Principal.Href))
            return Principal.Href;

        return "Unknown";
    }

    private string GetPrincipalTypeIcon()
    {
        if (Principal == null)
            return "?";

        if (Principal.IsAll)
            return "🌐";
        if (Principal.IsAuthenticated)
            return "👤";
        if (Principal.IsUnauthenticated)
            return "👻";
        if (Principal.IsSelf)
            return "👤";
        if (!string.IsNullOrEmpty(Principal.PropertyPrincipal))
            return "📋";
        if (!string.IsNullOrEmpty(Principal.Href))
            return "🔗";

        return "?";
    }

    private string GetPrincipalTypeColor()
    {
        if (Principal == null)
            return "#999999";

        if (Principal.IsAll)
            return "#0078D4"; // Blue
        if (Principal.IsAuthenticated)
            return "#107C10"; // Green
        if (Principal.IsUnauthenticated)
            return "#A80000"; // Red
        if (Principal.IsSelf)
            return "#8A2BE2"; // Purple
        if (!string.IsNullOrEmpty(Principal.PropertyPrincipal))
            return "#008272"; // Teal
        if (!string.IsNullOrEmpty(Principal.Href))
            return "#D83B01"; // Orange

        return "#999999";
    }

    [RelayCommand]
    private void Select()
    {
        if (_parentViewModel == null)
            return;

        // If this ACE is being selected (was not selected before)
        if (!IsSelected)
        {
            // Deselect all other ACEs
            foreach (var ace in _parentViewModel.Aces)
            {
                if (ace != this)
                    ace.IsSelected = false;
            }

            // Select this ACE
            IsSelected = true;
            _parentViewModel.SelectedAce = this;
        }
        // If this ACE is being deselected (was selected before)
        else
        {
            IsSelected = false;
            _parentViewModel.SelectedAce = null;
        }
    }

    [RelayCommand]
    private async Task EditAsync()
    {
        if (IsProtected)
            return;

        var editDialog = new EditAceDialog(this);
        var result = await editDialog.ShowDialog<bool>(_ownerWindow);

        if (result)
        {
            // Trigger change notification
            OnPropertyChanged(nameof(Privileges));
        }
    }

    [RelayCommand]
    private void Delete()
    {
        _parentViewModel?.RemoveAce(this);
    }

    public WebDavAce ToWebDavAce()
    {
        return new WebDavAce(
            Principal!,
            _originalAce.IsInverted,
            _originalAce.IsGrant,
            _originalAce.IsProtected,
            _originalAce.InheritedFrom,
            Privileges.ToArray()
        );
    }
}
