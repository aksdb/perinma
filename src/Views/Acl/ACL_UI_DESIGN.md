# ACL Management UI Design for CalDAV Calendars

## Overview

This design provides a polished, user-friendly interface for managing CalDAV ACL (Access Control List) permissions in the Perinma calendar application. The interface makes complex permission management feel intuitive through visual hierarchy, clear affordances, and thoughtful interaction patterns.

## Design Principles

### 1. Visual Hierarchy & Information Architecture
- **Header Section**: Calendar identity (color + name) and owner information immediately establishes context
- **Current User Privileges Banner**: Color-coded badges show user's access level at a glance (Read/Write/Manage Permissions)
- **ACE List**: Grouped access control entries with clear visual distinction between inherited/direct and protected/modifiable permissions

### 2. Color Palette
- **Grant (Allow)**: Green (#107C10) - positive action
- **Deny (Block)**: Red (#A80000) - negative action
- **Selection**: Blue (#0078D4) - active state
- **Principal Types**:
  - All Users: Blue (#0078D4)
  - Authenticated: Green (#107C10)
  - Anonymous: Red (#A80000)
  - Self (You): Purple (#8A2BE2)
  - Owner: Teal (#008272)
  - URL-based: Orange (#D83B01)

### 3. Typography
- **Headers**: Semibold, 18px for calendar name
- **Section Labels**: Semibold for "Who should have access?", "What permissions?"
- **Principal Names**: Semibold for easy scanning
- **Badges**: 10-12px, medium weight
- **Body Text**: Regular weight for readability

## Window Layout

```
┌─────────────────────────────────────────────────────────────┐
│ [🟦] My Calendar                    [─][□][✕]    │
│ Owner: https://server.com/principals/users/john            │
├─────────────────────────────────────────────────────────────┤
│ Your Access:                                                      │
│ [Read] [Write] [Manage Permissions]                                    │
├─────────────────────────────────────────────────────────────┤
│ [+ Add Permission] [Remove Selected]                         │
├─────────────────────────────────────────────────────────────┤
│ ┌─────────────────────────────────────────────────────────┐ │
│ │ [👤] John Doe                        [✏][✕] [Grant]│ │
│ │ [🔒 Protected]                                                  │ │
│ └─────────────────────────────────────────────────────────┘ │
│ ┌─────────────────────────────────────────────────────────┐ │
│ │ [👥] Marketing Team                        [✏][✕] [Grant]│ │
│ │                                                                 │ │
│ └─────────────────────────────────────────────────────────┘ │
│ ┌─────────────────────────────────────────────────────────┐ │
│ │ [📋] Inherited                            [Grant]        │ │
│ │ [🌐] All Users                                                │ │
│ └─────────────────────────────────────────────────────────┘ │
├─────────────────────────────────────────────────────────────┤
│                                                    [Cancel] [Save Changes]│
└─────────────────────────────────────────────────────────────┘
```

## Component Design

### 1. Calendar Header

**Purpose**: Establishes context and ownership

**Elements**:
- Color indicator box (24x24, 4px rounded corners)
- Calendar name (18px, Semibold)
- Owner URL (12px, Gray)

**Interactions**: None (read-only)

---

### 2. Current User Privileges Banner

**Purpose**: Shows what the current user can do (critical for permission awareness)

**Elements**:
- Label: "Your Access:" (Semibold)
- Color-coded badges:
  - **Green Read**: "Read" - Can view calendar
  - **Blue Write**: "Write" - Can create/modify events
  - **Purple Manage**: "Manage Permissions" - Can edit ACL
  - **Red View Only**: "View Only (cannot edit permissions)" - No write-ACL

**States**:
- When CanWriteAcl = false: Show "View Only" badge, disable Add/Remove buttons
- When CanWriteAcl = true: Show relevant badges (Read, Write, Manage)

**Interactions**: None (read-only indicator)

---

### 3. ACE List Item

**Purpose**: Displays a single permission entry with all relevant metadata

**Structure**:
```
┌────────────────────────────────────────────────────────────────┐
│ [👤] Principal Name  [🔒]          [✏] [✕]  [Grant] │
│   Type Icon    Badges    Actions          Grant/Deny       │
└────────────────────────────────────────────────────────────────┘
```

**Elements**:
- **Left Side**:
  - Type icon in colored box (20x20)
  - Principal display name (truncated if too long)
  - Badges (if applicable):
    - "Inherited" (gray background, clipboard icon)
    - "Protected" (gray background, lock icon)

- **Right Side**:
  - Action buttons (only if modifiable):
    - Edit (✏ pencil, 28x28, transparent background)
    - Delete (✕ X, 28x28, transparent background, red text)
  - Grant/Deny indicator (rounded pill):
    - Green background, white text for "Grant"
    - Red background, white text for "Deny"

**Visual States**:
- **Selected**: Blue border (2px)
- **Unselected**: Transparent border
- **Protected/Inherited**: Disabled edit/delete buttons
- **Modifiable**: Enabled edit/delete buttons

**Interactions**:
- Click anywhere: Toggle selection
- Hover: Slight background tint
- Edit button: Opens EditAceDialog
- Delete button: Shows confirmation, removes item

---

### 4. Add/Edit Dialog

**Purpose**: Form for creating or modifying an ACE

**Layout**:
```
┌─────────────────────────────────────────────┐
│ Add Permission                   [─][□][✕]│
├─────────────────────────────────────────────┤
│ Who should have access?                   │
│ [▼ Principal Type Dropdown]                │
│ [User/Group URL Input]                    │
├─────────────────────────────────────────────┤
│ What permissions?                           │
│ [Read Only] [Read/Write] [Full Access]     │
│ ┌─────────────────────────────────────┐     │
│ │ ☑ Read      ☑ Read ACL         │     │
│ │ ☑ Write     ☑ Write Properties │     │
│ │ ☑ Write Content                 │     │
│ │ ☑ Bind      ☑ Unbind           │     │
│ │ ☑ Unlock    ☑ Write ACL        │     │
│ └─────────────────────────────────────┘     │
├─────────────────────────────────────────────┤
│ Action:                                   │
│ ○ Grant  ● Deny                          │
├─────────────────────────────────────────────┤
│                                   [Cancel] [Add]│
└─────────────────────────────────────────────┘
```

**Components**:

**Principal Type Dropdown**:
Options with icons:
- 🌐 All Users
- 👤 Authenticated Users
- 👻 Anonymous Users
- 👤 You (Current User)
- 📋 Calendar Owner
- 🔗 User or Group URL

**URL Input**:
- Shows only when "User or Group URL" selected
- Textbox with placeholder: "https://server.com/principals/users/username"

**Privilege Selection**:

**Quick Presets**:
- **Read Only**: Read, Read ACL
- **Read/Write**: Read, Read ACL, Write, Write Properties, Write Content, Bind, Unbind, Unlock
- **Full Access**: All privileges including Write ACL

**Custom Checkboxes** (grouped for clarity):
- **Read**:
  - ☑ Read
  - ☑ Read ACL
- **Write**:
  - ☑ Write
  - ☑ Write Properties
  - ☑ Write Content
  - ☑ Bind
  - ☑ Unbind
  - ☑ Unlock
- **Admin**:
  - ☑ Write ACL

**Grant/Deny Toggle**:
- Radio buttons for Grant vs Deny
- Default: Grant

**Validation**:
- If "User or Group URL" selected: URL field required
- At least one privilege must be selected
- Protected/inherited ACEs cannot be modified (read-only)

---

### 5. Edit Dialog

**Purpose**: Modify existing ACE (principal is locked)

**Differences from Add Dialog**:
- Principal section is read-only (grayed out, opacity 0.7)
- Cannot change who the ACE applies to
- Only privileges and grant/deny can be modified
- Button text: "Save" instead of "Add"

---

## Interaction Flow

### Opening the Dialog

**From Calendar List** (Context Menu):
1. User right-clicks on a CalDAV calendar
2. "Manage Permissions..." option appears (only for CalDAV calendars)
3. Clicking opens AclManagementWindow

**Implementation**:
```csharp
// In CalendarListView.axaml - Add context menu to calendar item
<MenuItem Header="Manage Permissions..." 
          Command="{Binding ManagePermissionsCommand}" />
```

---

### Viewing Permissions

1. **Initial Load**:
   - Show loading indicator (spinner + "Loading permissions...")
   - Fetch ACL from server if not provided
   - Parse XML into view models
   - Populate ACE list

2. **Display State**:
   - Header shows calendar info
   - Banner shows current user's access level
   - ACE list displays all permissions
   - Add/Remove buttons enabled/disabled based on CanWriteAcl

---

### Adding a Permission

1. User clicks "+ Add Permission"
2. AddAceDialog opens (modal)
3. User selects principal type
4. User selects privileges (via presets or checkboxes)
5. User chooses Grant or Deny
6. User clicks "Add"
7. Dialog closes
8. New ACE appears in list
9. "Save Changes" button becomes enabled

---

### Editing a Permission

1. User clicks edit (✏) on a modifiable ACE
2. EditAceDialog opens (modal)
3. Principal is shown but read-only (grayed)
4. User modifies privileges
5. User changes Grant/Deny if needed
6. User clicks "Save"
7. Dialog closes
8. ACE in list updates
9. "Save Changes" button becomes enabled

---

### Removing a Permission

1. User selects an ACE (click to highlight)
2. User clicks "Remove Selected" OR
3. User clicks delete (✕) on the ACE
4. Confirmation dialog: "Are you sure you want to remove the permission for 'John Doe'?"
5. User confirms
6. ACE is removed from list
7. "Save Changes" button becomes enabled

**Constraints**:
- Protected ACEs: Delete button disabled, warning shown
- Inherited ACEs: Delete button disabled, "Inherited" badge visible
- Modifiable ACEs: Delete enabled

---

### Saving Changes

1. User makes modifications (add/edit/remove ACEs)
2. "Save Changes" button becomes enabled (gray → blue accent)
3. User clicks "Save Changes"
4. Loading indicator appears
5. AclManagementViewModel:
   - Builds WebDavAcl from modified ACEs
   - Converts to XML using WebDavAclParser
   - Sends ACL request to server via CalDavClient
6. If successful:
   - Success message: "Permissions have been saved successfully."
   - Window closes
   - Parent refreshes (if needed)
7. If fails:
   - Error message: "Failed to save permissions: {details}"
   - Window remains open (user can retry or cancel)

---

### Canceling

1. User clicks "Cancel"
2. If no changes: Window closes immediately
3. If changes exist: Optional confirmation (not shown in current design, could be added)
4. Window closes without saving

---

## Visual Design Details

### Spacing

| Context | Spacing |
|----------|----------|
| Window margin | 20px |
| Header to banner | 12px padding |
| Banner to toolbar | 12px padding |
| Toolbar to list | 8px margin |
| ACE item margin | 20px horizontal, 8px vertical |
| ACE item padding | 16px all sides |
| Buttons in stack | 10px spacing |
| Badge padding | 6-8px |

### Corner Radius

| Element | Radius |
|---------|---------|
| Color box | 4px |
| Type icon box | 4px |
| ACE item | 6px |
| Badges (Grant/Deny) | 4px |
| Buttons | 4px (Fluent default) |

### Icons

| Context | Icon | Font Size |
|---------|-------|-----------|
| Principal: All Users | 🌐 | 12px |
| Principal: Authenticated | 👤 | 10px |
| Principal: Anonymous | 👻 | 10px |
| Principal: Self | 👤 | 10px |
| Principal: Owner | 📋 | 10px |
| Principal: URL | 🔗 | 10px |
| Edit action | ✏ | 14px |
| Delete action | ✕ | 16px |
| Inherited badge | 📋 | 10px |
| Protected badge | 🔒 | 10px |

### Colors (Using Fluent Theme Palette)

| Purpose | Color |
|---------|--------|
| Grant background | #107C10 (Fluent green) |
| Deny background | #A80000 (Fluent red) |
| Selection border | #0078D4 (Fluent blue) |
| Protected badge text | Gray (#666666) |
| Protected badge background | SystemChromeLowColor |

---

## Error Handling

### User Without Write-ACL Permission

**Detection**:
- Check CanWriteAcl from current user privilege set
- If false, disable Add/Remove buttons

**Visual Feedback**:
- Show "View Only (cannot edit permissions)" badge
- Gray out toolbar buttons
- Hover tooltip: "You don't have permission to manage this calendar's permissions"

**Action**:
- User can still view the ACL list
- No editing allowed

---

### Server Rejects ACL Changes

**Detection**:
- CalDavClient.SetAclAsync throws exception

**Visual Feedback**:
- Error dialog: "Failed to save permissions: {server error message}"
- Window remains open with unsaved changes
- "Save Changes" button still enabled

**Recovery**:
- User can review and fix changes
- User can cancel to abandon changes

---

### Malformed ACL XML

**Detection**:
- XML parsing fails during load
- WebDavAclParser.ParseAcl throws exception

**Visual Feedback**:
- Error dialog: "Failed to load permissions: Invalid ACL data"
- Window closes (cannot display malformed data)

---

### Empty ACL List

**Display**:
- No ACEs to show
- Optional: "No permissions configured" placeholder text
- If CanWriteAcl: Show "+ Add Permission" button (enabled)

---

### Network Errors

**Detection**:
- HttpRequestException or timeout

**Visual Feedback**:
- Error dialog: "Failed to load permissions: {connection error}"
- Window closes (cannot proceed)

---

## Accessibility Considerations

### Keyboard Navigation

- Tab order: Header → Banner → Toolbar → ACE list → Buttons
- Enter/Space: Activate focused button
- Escape: Cancel/close dialogs
- Delete: Remove selected ACE (if allowed)

### Screen Reader Support

- Semantic labels for all interactive elements
- Announce state changes (selected, added, removed)
- Descriptive error messages

### High Contrast

- Use theme colors (Fluent theme handles high contrast)
- Icon badges have backgrounds for visibility
- Grant/Deny status uses both color and text

---

## Performance Considerations

### Lazy Loading

- ACL is loaded on-demand (not pre-fetched for all calendars)
- Icons and colors are computed on-demand

### Change Tracking

- PropertyChanged events on individual ACEs
- HasChanges flag prevents unnecessary saves
- Only modified ACEs are serialized to XML

---

## Future Enhancements

### 1. Permission Templates

Save frequently-used permission combinations as templates:
- "Reviewer": Read-only access
- "Contributor": Read/Write (no ACL management)
- "Owner": Full access

### 2. Bulk Operations

Select multiple ACEs and:
- Bulk delete
- Bulk modify privileges
- Bulk change Grant/Deny

### 3. Visual Graph

Show ACL structure as a tree/graph:
- Visualize inheritance
- See which permissions come from where
- Drag-and-drop to reorganize

### 4. Search/Filter

- Search by principal name/URL
- Filter by privilege type
- Filter by inherited/direct

---

## Implementation Checklist

- [x] AclManagementWindow.axaml - Main window layout
- [x] AclManagementWindow.axaml.cs - Window code-behind
- [x] AclManagementViewModel.cs - Main view model
- [x] AceItemViewModel.cs - Individual ACE view model
- [x] AddAceDialog.axaml - Add permission dialog
- [x] AddAceDialog.axaml.cs - Dialog code-behind
- [x] AddAceDialogViewModel.cs - Dialog view model
- [x] EditAceDialog.axaml - Edit permission dialog
- [x] EditAceDialog.axaml.cs - Dialog code-behind
- [x] EditAceDialogViewModel.cs - Dialog view model
- [x] SelectedBorderColorConverter.cs - Selection styling converter
- [ ] CalendarListView integration (context menu)
- [ ] CalendarViewModel extension (ACL data properties)
- [ ] SqliteStorage extension (ACL data persistence)
- [ ] Unit tests

---

## Usage Example

### Opening from Calendar List

```csharp
// In CalendarViewModel.cs
[RelayCommand]
private async Task ManagePermissionsAsync()
{
    // Get ACL data from storage (or pass from parent)
    var calendarDbo = await _storage.GetCalendarByIdAsync(Id.ToString());
    
    // Get ACL XML from data field
    var aclXml = await _storage.GetCalendarDataAsync(calendarDbo, "rawACL");
    var currentUserPrivilegeSetXml = await _storage.GetCalendarDataAsync(calendarDbo, "currentUserPrivilegeSet");
    var owner = await _storage.GetCalendarDataAsync(calendarDbo, "owner");
    
    // Open ACL management window
    await AclManagementWindow.ShowAsync(
        ownerWindow,
        Url, // Need to add Url to CalendarViewModel
        Name,
        Color,
        owner,
        aclXml,
        currentUserPrivilegeSetXml,
        _storage,
        _credentialManager);
}
```

### CalendarListView Integration

In `CalendarListView.axaml`, add context menu to calendar item:

```axaml
<!-- Around line 89-115, add MenuFlyout to Border -->
<Border Padding="5,4"
        Margin="0,2"
        Background="Transparent">
    <Border.ContextMenu>
        <ContextMenu>
            <MenuItem Header="Manage Permissions..."
                      Command="{Binding ManagePermissionsCommand}"
                      IsEnabled="{Binding IsCalDavCalendar}" />
        </ContextMenu>
    </Border.ContextMenu>
    
    <DockPanel>
        <!-- Existing content... -->
    </DockPanel>
</Border>
```

---

## Conclusion

This ACL management UI provides a polished, professional interface that makes complex CalDAV permission management feel simple and intuitive. The design emphasizes:

1. **Clarity**: Visual hierarchy and color coding make the permission structure immediately understandable
2. **Safety**: Confirmation dialogs, protected/inherited indicators prevent accidental changes
3. **Efficiency**: Quick presets, bulk operations (future), and keyboard shortcuts for power users
4. **Accessibility**: Full keyboard navigation and screen reader support
5. **Feedback**: Clear success/error messages and visual states at every step

The interface follows Fluent Design principles and integrates seamlessly with Perinma's existing design language while providing the specific functionality needed for CalDAV ACL management.
