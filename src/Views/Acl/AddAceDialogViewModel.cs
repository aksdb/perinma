using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using perinma.Services.CalDAV;

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

    [ObservableProperty]
    private int _selectedPrincipalTypeIndex = 0;

    [ObservableProperty]
    private string _principalUrl = string.Empty;

    [ObservableProperty]
    private bool _isGrant = true;

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

    public AddAceDialogViewModel(Window ownerWindow)
    {
        _ownerWindow = ownerWindow;

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

    partial void OnSelectedPrincipalTypeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(ShowUrlInput));
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

        var aceViewModel = new AceItemViewModel(ace, _ownerWindow);
        _ownerWindow.Close(aceViewModel);
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
