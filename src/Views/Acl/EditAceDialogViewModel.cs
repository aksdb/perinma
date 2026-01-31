using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using perinma.Services.CalDAV;

namespace perinma.Views.Acl;

/// <summary>
/// ViewModel for editing an existing ACE.
/// </summary>
public partial class EditAceDialogViewModel : ViewModelBase
{
    private readonly Window _ownerWindow;
    private readonly AceItemViewModel _originalAce;

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

    public string PrincipalDisplayText => _originalAce.PrincipalDisplayText;

    public string PrincipalTypeIcon => _originalAce.PrincipalTypeIcon;

    public string PrincipalTypeColor => _originalAce.PrincipalTypeColor;

    public EditAceDialogViewModel(Window ownerWindow, AceItemViewModel originalAce)
    {
        _ownerWindow = ownerWindow;
        _originalAce = originalAce;

        // Initialize from original ACE
        IsGrant = originalAce.IsGrant;

        // Initialize privileges
        InitializePrivileges(originalAce.Privileges);
    }

    private void InitializePrivileges(ObservableCollection<WebDavPrivilege> privileges)
    {
        CanRead = privileges.Any(p => p.FullName == "DAV:read");
        CanReadAcl = privileges.Any(p => p.FullName == "DAV:read-acl");
        CanWrite = privileges.Any(p => p.FullName == "DAV:write");
        CanWriteProperties = privileges.Any(p => p.FullName == "DAV:write-properties");
        CanWriteContent = privileges.Any(p => p.FullName == "DAV:write-content");
        CanBind = privileges.Any(p => p.FullName == "DAV:bind");
        CanUnbind = privileges.Any(p => p.FullName == "DAV:unbind");
        CanUnlock = privileges.Any(p => p.FullName == "DAV:unlock");
        CanWriteAcl = privileges.Any(p => p.FullName == "DAV:write-acl");
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
    /// Saves changes and closes dialog.
    /// </summary>
    [RelayCommand]
    private void Save()
    {
        // Build privilege list
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

        // Update the original ACE's privileges
        _originalAce.Privileges.Clear();
        foreach (var privilege in privileges)
        {
            _originalAce.Privileges.Add(privilege);
        }

        _ownerWindow.Close(true);
    }

    /// <summary>
    /// Cancels and closes dialog.
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        _ownerWindow.Close(false);
    }
}
