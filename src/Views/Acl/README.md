# ACL Management UI - Implementation Summary

## Files Created

### Main Window
1. **AclManagementWindow.axaml** - Main window layout for managing ACLs
   - Calendar header with color and name
   - Current user privileges banner
   - Toolbar for Add/Remove actions
   - Scrollable ACE list
   - Save/Cancel buttons

2. **AclManagementWindow.axaml.cs** - Window code-behind
   - Static `ShowAsync()` method for opening the window
   - Properly handles window lifecycle

3. **AclManagementViewModel.cs** - Main view model
   - Manages ACE list and changes
   - Handles loading ACL from XML or server
   - Implements permission checks (CanRead, CanWrite, CanWriteAcl)
   - Save/Cancel logic

### ACE Item
4. **AceItemViewModel.cs** - Individual ACE view model (embedded in AclManagementViewModel.cs)
   - Represents a single access control entry
   - Displays principal info, privileges, grant/deny status
   - Shows inherited/protected badges
   - Edit and delete commands

### Add Permission Dialog
5. **AddAceDialog.axaml** - Dialog layout for adding new ACE
   - Principal type dropdown (All, Authenticated, Anonymous, Self, Owner, URL)
   - URL input for custom principals
   - Privilege selection with presets (Read Only, Read/Write, Full Access)
   - Custom privilege checkboxes
   - Grant/Deny toggle

6. **AddAceDialog.axaml.cs** - Dialog code-behind

7. **AddAceDialogViewModel.cs** - Dialog view model
   - Manages principal type selection
   - Preset privilege buttons
   - Custom privilege checkboxes
   - Grant/Deny toggle
   - Creates new AceItemViewModel

### Edit Permission Dialog
8. **EditAceDialog.axaml** - Dialog layout for editing existing ACE
   - Read-only principal display (grayed out)
   - Same privilege selection as Add dialog
   - Grant/Deny toggle
   - "Save" button instead of "Add"

9. **EditAceDialog.axaml.cs** - Dialog code-behind

10. **EditAceDialogViewModel.cs** - Dialog view model
    - Initializes from existing AceItemViewModel
    - Modifies privileges and grant/deny
    - Updates original ACE on save

### Converters
11. **SelectedBorderColorConverter.cs** - Converts selection state to border color
    - Blue border when selected
    - Transparent when unselected

### Documentation
12. **ACL_UI_DESIGN.md** - Comprehensive design document
    - Full UI specifications
    - Interaction flows
    - Error handling strategies
    - Accessibility considerations

## Design Highlights

### Visual Hierarchy
1. **Calendar Header** - Establishes context with color indicator and name
2. **User Access Banner** - Color-coded badges (Read/Write/Manage Permissions)
3. **Toolbar** - Add/Remove actions (disabled if no write-ACL permission)
4. **ACE List** - Permission entries with clear visual distinction:
   - Type icons with color coding
   - Principal names
   - Inherited/Protected badges
   - Edit/Delete buttons
   - Grant/Deny pills

### Color Palette
| Purpose | Color |
|---------|--------|
| Grant | #107C10 (Green) |
| Deny | #A80000 (Red) |
| Selection | #0078D4 (Blue) |
| Principal: All | #0078D4 (Blue) |
| Principal: Authenticated | #107C10 (Green) |
| Principal: Anonymous | #A80000 (Red) |
| Principal: Self | #8A2BE2 (Purple) |
| Principal: Owner | #008272 (Teal) |
| Principal: URL | #D83B01 (Orange) |

### Permission Awareness
The UI respects current user's permissions:
- **CanWriteAcl = true**: Full editing enabled
- **CanWriteAcl = false**: Read-only mode, "View Only" badge shown
- **Protected ACEs**: Cannot be modified (lock badge)
- **Inherited ACEs**: Cannot be modified (clipboard badge)

## Usage

### Opening the Window

```csharp
// From CalendarViewModel or similar
await AclManagementWindow.ShowAsync(
    ownerWindow: App.Current.MainWindow,
    calendarUrl: "https://server.com/calendars/mycalendar/",
    calendarName: "My Calendar",
    calendarColor: "#FF5733",
    ownerUrl: "https://server.com/principals/users/john",
    aclXml: "<d:acl>...</d:acl>", // Optional, will fetch if null
    currentUserPrivilegeSetXml: "<d:current-user-privilege-set>...</d:current-user-privilege-set>", // Optional
    storage: new SqliteStorage(databaseService, credentialManager),
    credentialManager: credentialManager
);
```

### Adding a Permission

1. Click "+ Add Permission" button
2. Select principal type (dropdown with icons)
3. If "User or Group URL", enter URL
4. Select privileges:
   - Quick presets: Read Only, Read/Write, Full Access
   - Or custom: Check/uncheck individual privileges
5. Choose Grant or Deny
6. Click "Add"

### Editing a Permission

1. Click edit (✏) button on an ACE
2. Principal is read-only (grayed out)
3. Modify privileges via presets or checkboxes
4. Change Grant/Deny if needed
5. Click "Save"

### Removing a Permission

1. Click delete (✕) button OR
2. Click ACE to select, then "Remove Selected"
3. Confirm in dialog
4. ACE removed from list

### Saving Changes

1. Make any changes (add/edit/remove)
2. "Save Changes" button becomes enabled
3. Click "Save Changes"
4. Changes sent to server
5. Success message on completion, window closes

## Integration Steps

### Step 1: Extend CalendarViewModel

Add properties for ACL data:

```csharp
// In CalendarViewModel.cs
[ObservableProperty]
private string? _url;  // Need to add this

[ObservableProperty]
private string? _aclXml;

[ObservableProperty]
private string? _currentUserPrivilegeSetXml;

[ObservableProperty]
private string? _owner;

[ObservableProperty]
private bool _isCalDav;  // Track calendar type
```

### Step 2: Load ACL Data in CalendarListViewModel

When loading calendars from database:

```csharp
// In CalendarListViewModel.cs, LoadCalendarsAsync()
var calendarDbo = await _storage.GetCalendarByIdAsync(calendar.Id.ToString());

var aclXml = await _storage.GetCalendarDataAsync(calendarDbo, "rawACL");
var currentUserPrivilegeSetXml = await _storage.GetCalendarDataAsync(calendarDbo, "currentUserPrivilegeSet");
var owner = await _storage.GetCalendarDataAsync(calendarDbo, "owner");

calendarViewModel.Url = calendarDbo.ExternalId; // Need to store this
calendarViewModel.AclXml = aclXml;
calendarViewModel.CurrentUserPrivilegeSetXml = currentUserPrivilegeSetXml;
calendarViewModel.Owner = owner;
calendarViewModel.IsCalDav = (account.AccountTypeEnum == AccountType.CalDav);
```

### Step 3: Add Context Menu to CalendarListView

Add "Manage Permissions..." option (CalDAV only):

```axaml
<!-- In CalendarListView.axaml, within calendar item Border -->
<Border Padding="5,4"
        Margin="0,2"
        Background="Transparent">
    <Border.ContextMenu>
        <ContextMenu>
            <MenuItem Header="Manage Permissions..."
                      Command="{Binding ManagePermissionsCommand}"
                      IsVisible="{Binding IsCalDav}" />
        </ContextMenu>
    </Border.ContextMenu>
    
    <!-- Existing DockPanel content... -->
</Border>
```

### Step 4: Implement ManagePermissionsCommand

Add command to CalendarViewModel:

```csharp
// In CalendarViewModel.cs
[RelayCommand]
private async Task ManagePermissionsAsync()
{
    // Pass required data to ACL window
    var result = await AclManagementWindow.ShowAsync(
        ownerWindow: App.Current.MainWindow,
        calendarUrl: Url ?? "",
        calendarName: Name,
        calendarColor: Color,
        ownerUrl: Owner,
        aclXml: AclXml,
        currentUserPrivilegeSetXml: CurrentUserPrivilegeSetXml,
        storage: _storage,  // Need to inject this
        credentialManager: _credentialManager  // Need to inject this
    );
    
    if (result)
    {
        // Refresh calendar data after successful save
        // This could trigger a re-sync
    }
}
```

### Step 5: Inject Services into CalendarListViewModel

Update CalendarListViewModel constructor:

```csharp
// In CalendarListViewModel.cs
public CalendarListViewModel(
    SqliteStorage storage,
    IGoogleCalendarService googleCalendarService,
    CredentialManagerService credentialManager,
    CalendarWeekViewModel calendarWeekViewModel)
{
    _storage = storage;
    _googleCalendarService = googleCalendarService;
    _credentialManager = credentialManager;
    _calendarWeekViewModel = calendarWeekViewModel;
    
    // ... existing code ...
}

// When creating CalendarViewModels, pass services
var calendarViewModel = new CalendarViewModel
{
    Id = Guid.Parse(calendar.CalendarId),
    Name = calendar.Name,
    Color = calendar.Color,
    Enabled = calendar.Enabled != 0,
    Url = calendar.ExternalId,  // Add this
    Storage = storage,  // Add this
    CredentialManager = credentialManager  // Add this
};
```

## Dependencies

The ACL Management UI depends on:

1. **Avalonia UI** - Window framework
2. **CommunityToolkit.Mvvm** - MVVM source generators ([ObservableProperty], [RelayCommand])
3. **Perinma.Services.CalDAV** - WebDavAcl, WebDavAce, WebDavPrivilege, WebDavPrincipal, WebDavAclParser, CalDavClient
4. **Perinma.Storage** - SqliteStorage (for ACL data retrieval)
5. **Perinma.Services** - CredentialManagerService

## Testing Checklist

- [ ] Open ACL window for calendar with full permissions
- [ ] Open ACL window for calendar with read-only access
- [ ] Verify "View Only" badge appears when CanWriteAcl = false
- [ ] Verify Add/Remove buttons are disabled when CanWriteAcl = false
- [ ] Add new ACE with "All Users" principal
- [ ] Add new ACE with custom URL principal
- [ ] Use preset buttons for privileges
- [ ] Use custom checkboxes for privileges
- [ ] Edit existing ACE
- [ ] Remove existing ACE
- [ ] Verify protected ACEs cannot be modified
- [ ] Verify inherited ACEs cannot be modified
- [ ] Save changes successfully
- [ ] Cancel without saving
- [ ] Cancel with unsaved changes (optional confirmation)
- [ ] Verify Grant/Deny colors are correct
- [ ] Verify principal type icons and colors
- [ ] Test keyboard navigation (Tab, Enter, Escape)
- [ ] Test with empty ACL list
- [ ] Test with malformed ACL XML
- [ ] Test server rejection of ACL changes
- [ ] Test network errors

## Known Issues / Future Enhancements

1. **CalendarViewModel Extension** - Need to add URL, ACL XML properties, and service references
2. **Service Injection** - CalendarViewModel needs access to SqliteStorage and CredentialManagerService
3. **Refresh After Save** - After successful ACL save, may need to refresh calendar data
4. **Permission Templates** - Save frequently-used permission combinations
5. **Bulk Operations** - Select multiple ACEs for batch operations
6. **Visual Graph** - Tree view showing inheritance hierarchy
7. **Search/Filter** - Filter ACEs by principal or privilege

## License

This code is part of the Perinma calendar application.
