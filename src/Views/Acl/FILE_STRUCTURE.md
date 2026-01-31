# ACL Management UI - File Structure & Relationships

## File Structure

```
src/Views/Acl/
│
├── AclManagementWindow.axaml              # Main window layout (XAML)
│   ├── Header (Calendar name + color)
│   ├── Current User Privileges Banner
│   ├── Toolbar (Add/Remove buttons)
│   ├── ACE List (Scrollable ItemsControl)
│   └── Action Buttons (Save/Cancel)
│
├── AclManagementWindow.axaml.cs           # Window code-behind
│   └── ShowAsync() factory method
│
├── AclManagementViewModel.cs               # Main view model
│   ├── Properties: CalendarName, Color, Owner, etc.
│   ├── Observable: Aces (AceItemViewModel[])
│   ├── Commands: AddAce, RemoveAce, Save, Cancel
│   ├── Methods: LoadAclDataAsync, FetchFromServer, SaveToServer
│   └── Computed: CanRead, CanWrite, CanWriteAcl
│
├── AddAceDialog.axaml                    # Add permission dialog (XAML)
│   ├── Principal Type Dropdown
│   ├── URL Input (conditional)
│   ├── Privilege Presets (buttons)
│   ├── Custom Privileges (checkboxes)
│   ├── Grant/Deny Toggle
│   └── Add/Cancel Buttons
│
├── AddAceDialog.axaml.cs                 # Dialog code-behind
│   └── InitializeComponent()
│
├── AddAceDialogViewModel.cs               # Add dialog view model
│   ├── Properties: SelectedPrincipalType, PrincipalUrl
│   ├── Properties: CanRead, CanWrite, CanWriteAcl, etc.
│   ├── Commands: SetPreset, Add, Cancel
│   └── Method: CreateAceFromInput()
│
├── EditAceDialog.axaml                   # Edit permission dialog (XAML)
│   ├── Principal Display (read-only, grayed)
│   ├── Privilege Presets (buttons)
│   ├── Custom Privileges (checkboxes)
│   ├── Grant/Deny Toggle
│   └── Save/Cancel Buttons
│
├── EditAceDialog.axaml.cs                # Dialog code-behind
│   └── InitializeComponent()
│
├── EditAceDialogViewModel.cs              # Edit dialog view model
│   ├── Properties: IsGrant
│   ├── Properties: CanRead, CanWrite, CanWriteAcl, etc.
│   ├── Commands: SetPreset, Save, Cancel
│   └── Method: UpdateOriginalAce()
│
├── SelectedBorderColorConverter.cs          # Value converter
│   └── Convert(bool isSelected) → BorderBrush
│
├── README.md                             # Implementation guide
├── ACL_UI_DESIGN.md                      # Design specification
└── DELIVERY_SUMMARY.md                   # Delivery summary
```

## Component Relationships

```
┌─────────────────────────────────────────────────────────────────┐
│                    AclManagementWindow                        │
│  ┌─────────────────────────────────────────────────────────┐  │
│  │              AclManagementViewModel                    │  │
│  │  ┌──────────────────────────────────────────────────┐  │  │
│  │  │  ObservableCollection<AceItemViewModel> Aces   │  │  │
│  │  └──────────────────────────────────────────────────┘  │  │
│  │         ▲                                            │  │
│  │         │                                            │  │
│  │         │ (1:N)                                      │  │
│  │         │                                            │  │
│  │  ┌──────┴────────────────────────────────────────┐   │  │
│  │  │         AceItemViewModel[0..N]              │   │  │
│  │  │  - Principal, Privileges, IsGrant, etc.      │   │  │
│  │  │  - Select, Edit, Delete Commands             │   │  │
│  │  └─────────────────────────────────────────────┘   │  │
│  │                                                   │  │
│  │  AddAceCommand ▼                                    │  │
│  │  ┌────────────────────────────────────────────────┐   │  │
│  │  │         AddAceDialog                       │   │  │
│  │  │  ┌────────────────────────────────────────┐  │  │
│  │  │  │     AddAceDialogViewModel           │  │  │
│  │  │  │  - PrincipalType, Url             │  │  │
│  │  │  │  - Privilege flags               │  │  │
│  │  │  │  - SetPreset, Add Commands       │  │  │
│  │  │  └───────────────────────────────────────┘  │  │
│  │  │  - Returns AceItemViewModel                 │   │  │
│  │  └─────────────────────────────────────────────┘   │  │
│  │                                                   │  │
│  │  EditCommand ▼                                      │  │
│  │  ┌────────────────────────────────────────────────┐   │  │
│  │  │         EditAceDialog                       │   │  │
│  │  │  ┌────────────────────────────────────────┐  │  │
│  │  │  │    EditAceDialogViewModel           │  │  │
│  │  │  │  - (Principal readonly)            │  │  │
│  │  │  │  - Privilege flags               │  │  │
│  │  │  │  - SetPreset, Save Commands      │  │  │
│  │  │  └───────────────────────────────────────┘  │  │
│  │  │  - Modifies existing AceItemViewModel       │   │  │
│  │  └─────────────────────────────────────────────┘   │  │
│  └─────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
```

## Data Flow

### Loading ACL
```
CalendarListViewModel
    │
    ▼
LoadCalendarsAsync()
    │
    ▼
GetCalendarByIdAsync() → CalendarDbo
    │
    ▼
GetCalendarDataAsync("rawACL") → aclXml
GetCalendarDataAsync("currentUserPrivilegeSet") → currentUserPrivilegeSetXml
GetCalendarDataAsync("owner") → owner
    │
    ▼
AclManagementWindow.ShowAsync(
    calendarUrl, calendarName, calendarColor, owner, aclXml, currentUserPrivilegeSetXml
)
    │
    ▼
AclManagementViewModel constructor
    │
    ▼
LoadAclDataAsync(aclXml)
    │
    ├─────────────────────────────────────────┐
    │                                     │
    ▼                                     ▼
aclXml provided?                    FetchFromServerAsync()
    │                                     │
    │                                     ▼
    ▼                                 CalDavClient.GetAclAsync()
ParseAclXml(aclXml)                    │
    │                                     │
    ▼                                     │
WebDavAclParser.ParseAcl()                │
    │                                     │
    └─────────────────┬─────────────────────┘
                      │
                      ▼
                Populate Aces collection
                      │
                      ▼
                UI displays ACE list
```

### Saving Changes
```
User clicks Save Changes
    │
    ▼
SaveCommand.Execute()
    │
    ▼
Build WebDavAcl from Aces
    │
    ▼
WebDavAclParser.BuildAclXml() → aclXml
    │
    ▼
CalDavClient.SetAclAsync(calendarUrl, acl)
    │
    ├────────────────────┬────────────────────┐
    │                    │                    │
    ▼                    ▼                    ▼
Success              Error              Network Error
    │                    │                    │
    ▼                    ▼                    ▼
Success message      Error message        Error message
Close window          Window stays open   Window closes
```

### Adding Permission
```
User clicks + Add Permission
    │
    ▼
AddAceCommand.Execute()
    │
    ▼
AddAceDialog.ShowDialog()
    │
    ▼
User fills in form:
    - Select principal type
    - Enter URL (if needed)
    - Select privileges (preset or custom)
    - Choose Grant/Deny
    │
    ▼
User clicks Add
    │
    ▼
AddAceDialogViewModel.AddCommand.Execute()
    │
    ▼
Create WebDavAce from input
    │
    ▼
Create AceItemViewModel(WebDavAce)
    │
    ▼
Close dialog (returns AceItemViewModel)
    │
    ▼
AclManagementViewModel receives result
    │
    ▼
Aces.Add(aceViewModel)
    │
    ▼
HasChanges = true
    │
    ▼
SaveChanges button enabled
```

### Editing Permission
```
User clicks edit (✏) on ACE
    │
    ▼
EditCommand.Execute()
    │
    ▼
EditAceDialog.ShowDialog(pass existing AceItemViewModel)
    │
    ▼
User modifies privileges
    │
    ▼
User clicks Save
    │
    ▼
EditAceDialogViewModel.SaveCommand.Execute()
    │
    ▼
Update original AceItemViewModel.Privileges
    │
    ▼
Close dialog (returns true)
    │
    ▼
AclManagementViewModel receives result
    │
    ▼
HasChanges = true
    │
    ▼
SaveChanges button enabled
```

## Dependencies

### External Dependencies
```
Avalonia.UI
└── Window, UserControl, Button, etc.

CommunityToolkit.Mvvm
├── ObservableProperty (generates properties)
├── RelayCommand (generates commands)
└── ObservableObject (base class)

Perinma.Services.CalDAV
├── WebDavAcl
├── WebDavAce
├── WebDavPrincipal
├── WebDavPrivilege
├── WebDavCurrentUserPrivilegeSet
├── WebDavAclParser
└── CalDavClient

Perinma.Storage
└── SqliteStorage

Perinma.Services
└── CredentialManagerService

Perinma.Views.MessageBox
└── MessageBoxWindow
```

### Internal Dependencies
```
AclManagementWindow
    ▼
AclManagementViewModel
    ├──── AceItemViewModel
    │
    ├──── AddAceDialog
    │         ▼
    │    AddAceDialogViewModel
    │
    ├──── EditAceDialog
    │         ▼
    │    EditAceDialogViewModel
    │
    └──── SelectedBorderColorConverter
```

## Integration Points

### Where to Integrate
```
CalendarListView.axaml
    ▼
CalendarViewModel
    ▼
CalendarListViewModel
    ▼
AclManagementWindow.ShowAsync()
```

### Required Properties on CalendarViewModel
```csharp
// Need to add these to CalendarViewModel.cs
[ObservableProperty] private string? _url;
[ObservableProperty] private string? _aclXml;
[ObservableProperty] private string? _currentUserPrivilegeSetXml;
[ObservableProperty] private string? _owner;
[ObservableProperty] private bool _isCalDav;
[ObservableProperty] private SqliteStorage _storage;
[ObservableProperty] private CredentialManagerService _credentialManager;
```

### Required Command on CalendarViewModel
```csharp
// Need to add this command to CalendarViewModel.cs
[RelayCommand]
private async Task ManagePermissionsAsync()
{
    await AclManagementWindow.ShowAsync(
        App.Current.MainWindow,
        Url,
        Name,
        Color,
        Owner,
        AclXml,
        CurrentUserPrivilegeSetXml,
        Storage,
        CredentialManager
    );
}
```

## Building

### Build Command
```bash
cd /home/schnandr/Development/C#/perinma
dotnet build src/perinma.csproj
```

### Expected Output
- XAML files are compiled into partial classes
- InitializeComponent() methods are generated
- All LSP errors related to InitializeComponent are resolved
- All files compile successfully

## Testing

### Manual Testing Steps
1. Right-click on a CalDAV calendar
2. Select "Manage Permissions..."
3. Verify window opens with correct calendar info
4. Verify current user privileges are shown correctly
5. Add a new ACE with "All Users" principal
6. Save changes
7. Verify success message and window closes
8. Reopen to verify changes persisted

### Unit Tests (To Be Created)
```
tests/AclTests/
├── AclManagementViewModelTests.cs
├── AceItemViewModelTests.cs
├── AddAceDialogViewModelTests.cs
└── EditAceDialogViewModelTests.cs
```

## Next Steps

1. **Integration** - Add properties and command to CalendarViewModel
2. **UI Integration** - Add context menu to CalendarListView
3. **Testing** - Manual testing and unit tests
4. **Polish** - Fine-tune spacing, colors, animations
5. **Documentation** - Update user manual with ACL management section
