# Email client design and implementation plan

## Goal
Add mail support to perinma in a way that fits the existing main-window structure, reuses the current provider/sync/storage patterns, uses AtomUI in the UI, and renders message bodies safely in either plaintext or HTML via Avalonia WebView with external resources blocked by default.

## Current architecture observations
- `MainWindow` currently exposes two top-level modes: `Calendar` and `Contacts`, with menu + segmented-switcher wiring in `src/Views/Main/MainWindow.axaml` and `MainWindowViewModel`.
- Settings/account management is organized around provider-specific account setup in `SettingsWindow`, `AccountListViewModel`, and `AddAccountWizardViewModel`.
- Sync is already split into provider abstractions plus orchestration services:
  - `ICalendarProvider` + `SyncService`
  - `IContactProvider` + `ContactSyncService`
- Local persistence uses SQLite migrations plus `SqliteStorage`, with generic `data` JSON blobs per entity and provider raw payload retention for later hydration/editing.
- Rich text already exists for calendar descriptions (`RichText`, `RichTextView`), but mail needs stricter rendering policy and richer body/attachment semantics than the current calendar-description path.
- Settings already persist last active main view and layout state, so mail can slot into that mechanism instead of inventing a separate shell.

## Recommended architectural direction
Do **not** bolt mail on as isolated `GoogleMail` / `JmapMail` account types.

Instead:
1. Refactor accounts to separate **provider kind** from **capabilities**.
2. Keep one Google account that can own calendar, contacts, and mail capabilities.
3. Add JMAP as a provider kind with mail capability.
4. Add a dedicated mail domain/storage/sync/UI stack parallel to calendar and contacts.

### Why this direction
- It matches the user mental model: one Google identity, not three pseudo-accounts.
- It avoids duplicated credentials and scope drift.
- It prevents the settings UI from degenerating into provider-feature permutations.
- It gives a clean path for future IMAP/SMTP or Exchange support without rebreaking account semantics.

## Domain design

### Account model
Introduce:
- `AccountProviderKind` enum:
  - `Google`
  - `CalDav`
  - `CardDav`
  - `Jmap`
- `AccountCapability` flags:
  - `Calendar`
  - `Contacts`
  - `Mail`

Account storage should persist both provider kind and capability flags.

Migration mapping for existing data:
- existing `Google` -> provider `Google`, capabilities `Calendar | Contacts`
- existing `CalDav` -> provider `CalDav`, capabilities `Calendar`
- existing `CardDav` -> provider `CardDav`, capabilities `Contacts`

### Mail model
Add explicit mail models; do not overload `CalendarEvent`/`RichText`.

Recommended models:
- `Mailbox`
  - local id
  - account id
  - external id
  - name
  - role/system type (`inbox`, `sent`, `trash`, etc. when known)
  - parent mailbox id/external id
  - unread count
  - total count
  - enabled flag
  - sync state/data blob
- `MailThread`
  - local id
  - account id
  - external id / provider thread id
  - subject summary
  - participants summary
  - latest message timestamp
  - unread count
  - has attachments
  - preview/snippet
  - message count
  - data blob
- `MailMessage`
  - local id
  - account id
  - thread id
  - external id / provider message id
  - internet message id
  - subject
  - from/reply-to/sender
  - to/cc/bcc collections
  - sent/received timestamps
  - preview/snippet
  - unread/starred/answered/draft/flagged state
  - raw provider payload
  - normalized body summary metadata
  - data blob
- `MailBody`
  - `PlainText`
  - `Html`
  - `HasExternalResources`
  - `HasBlockedContent`
  - `PreferredBodyKind`
- `MailAttachment`
  - local id
  - message id
  - external/blob id
  - file name
  - MIME type
  - size
  - `IsInline`
  - `ContentId`
  - cache metadata / local content key

Provider raw payloads should still be stored so the app can rehydrate provider-specific details without losing fidelity.

## Storage design

### New tables
Add mail-focused tables and keep the current SQLite style:
- `mailbox`
- `mail_thread`
- `mail_message`
- `mail_message_mailbox` (many-to-many label/mailbox membership)
- `mail_attachment`
- optionally `mail_identity` if compose/send is in scope

### Query/storage principles
- Store enough normalized columns for fast list rendering and filtering.
- Store raw provider payload in `data.rawData` for round-tripping and debugging.
- Keep mailbox/thread query paths local-first.
- Cache bodies and attachments lazily; do not require full-body sync before the UI becomes useful.

### Sync state storage
- JMAP: persist per-account/per-mailbox state tokens from session/mailbox/email change APIs.
- Gmail: persist `historyId` and paging cursors where needed.
- Store provider-specific cursors in existing JSON `data` blobs rather than baking provider-specific columns into schema.

## Provider/service design

### New provider abstraction
Add `IMailProvider` parallel to the existing provider interfaces.

Recommended shape:
- `GetMailboxesAsync(accountId, syncState, cancellationToken)`
- `SyncMessagesAsync(accountId, mailboxExternalId, syncState, window, cancellationToken)`
- `HydrateMessageAsync(accountId, messageExternalId, bodyMode, cancellationToken)`
- `DownloadAttachmentAsync(accountId, messageExternalId, attachmentExternalId, cancellationToken)`
- `TestConnectionAsync(accountId, cancellationToken)`
- mutation methods only if initial scope includes message state changes / send

Provider output should be provider-agnostic DTOs similar to `ProviderCalendar` / `ProviderContact`.

### Gmail provider
Add:
- `GoogleMailService`
- `GoogleMailProvider`

Use Gmail REST API and .NET client libraries to match the existing Google service pattern.

Recommended Gmail handling:
- labels -> `Mailbox`
- messages -> local `MailMessage`
- thread ids -> local `MailThread`
- history API for incremental sync
- bodies/attachments fetched on demand, with metadata-first sync

### JMAP provider
Add:
- `JmapMailService`
- `JmapMailProvider`

Use direct `HttpClient` + JSON requests, similar in spirit to the existing CalDAV/CardDAV implementations.

Recommended JMAP handling:
- session discovery from configured session URL
- `Mailbox/*` for mailbox tree sync
- `Email/queryChanges` / `Email/get` for incremental message sync
- `Thread/get` only when needed for richer thread hydration
- blob download for attachments and inline parts

### Sync orchestration
Add `MailSyncService` parallel to the existing sync services.

Responsibilities:
- enumerate accounts with mail capability
- sync enabled mailboxes
- upsert mailbox/thread/message summaries
- invalidate/rebuild thread summaries incrementally
- emit progress messages for status bar + mail view
- reuse current auto-sync cadence before considering push

Recommended first-cut sync policy:
- full mailbox list sync
- metadata-only message sync for enabled mailboxes
- body/attachment hydration on demand
- local cache retained for recent mail and explicitly opened messages

## UI design

## Shell integration
Add a third main mode: `Mail`.

Changes:
- `MainWindow.axaml`: add `Mail` menu item and segmented item beside Calendar/Contacts
- `MainWindowViewModel`: add `Mail` mode, persisted last active view, and a `MailViewModel`
- settings persistence: add mail pane widths and mail-selected mailbox/view state

### Mail workspace layout
Follow the existing Contacts pattern because it already fits a three-pane information workflow.

Recommended layout:
1. **Left pane: mailbox tree**
   - grouped by account
   - system mailboxes first, custom mailboxes after
   - unread badges
   - enabled/visible toggles if mailbox-level sync control is needed
2. **Center pane: thread list**
   - search box
   - filter row (Unread, Starred, Has Attachments) using AtomUI controls
   - thread summaries with sender, subject, snippet, date, unread state
3. **Right pane: thread/message preview**
   - selected thread rendered as stacked message cards
   - header actions (mark read/unread, star, archive/delete if in scope)
   - body mode segmented control: `Auto`, `Plain text`, `HTML`
   - explicit `Load external resources` button per message when blocked content exists

### AtomUI usage
Use AtomUI components for all new app chrome and interaction surfaces:
- `Button`
- `TextBox` / `LineEdit`
- `Segmented`
- `ListBox`
- `MenuItem`
- `ProgressBar`
- `Form` / `FormItem` for account setup
- AtomUI window/dialog primitives where new dialogs are needed

### Account setup UI
Extend the current account settings flow instead of adding a parallel mail-only settings screen.

Recommended approach:
- replace the provider-only account wizard with provider + capability selection
- Google account wizard should allow enabling mail capability on an existing Google account and trigger scope upgrade if required
- JMAP wizard should collect:
  - account name
  - session URL
  - auth mode
  - username
  - password/app password or bearer token

## Message rendering design

### Rendering modes
For every message, support:
- plaintext rendering
- HTML rendering in Avalonia WebView (`Avalonia.Controls.WebView` / `NativeWebView`)

Selection policy:
- `Auto`: prefer HTML when available, otherwise plaintext
- user can switch to plaintext even when HTML exists
- user can switch back to HTML for the same message

### Security model
The HTML path must be safe by construction.

#### Non-negotiable rules
- never navigate the WebView directly to remote message content
- never allow scripts from message content
- never auto-load external resources
- never persist blanket allow-lists for all mail unless explicitly designed later
- external-resource allowance must be scoped to the currently selected message

#### Recommended implementation
Build a dedicated `SecureMailHtmlView` around `NativeWebView`.

Pipeline:
1. Parse provider HTML body into a normalized HTML document.
2. Sanitize it:
   - remove `<script>` tags
   - remove inline event handlers (`onclick`, etc.)
   - strip `javascript:` / `data:` URL abuse where unsafe
   - remove forms, iframes, object/embed, base tags, remote CSS imports, and active content
3. Rewrite allowed inline resources (`cid:` / cached attachments) to an app-owned URI scheme such as `perinma-mail://...`.
4. Inject a restrictive CSP into the generated document, e.g.:
   - `default-src 'none'`
   - `img-src perinma-mail: data:`
   - `style-src 'unsafe-inline'`
   - `font-src perinma-mail: data:`
   - `media-src perinma-mail: data:`
   - `connect-src 'none'`
   - `frame-src 'none'`
   - `form-action 'none'`
   - `base-uri 'none'`
5. Render with `NavigateToString()`.
6. Intercept resource requests and serve only:
   - app-owned rewritten inline resources
   - explicitly user-approved external resources for that specific message

#### External resources
- Detect blocked external resources during sanitization/rewrite.
- Show `Load external resources` only when relevant.
- When clicked:
  - record an in-memory approval keyed by message id
  - extend the allow-list for that message only
  - reload the sanitized HTML with the approved resource policy

#### Platform hardening
Where the underlying native WebView exposes stronger settings via platform interop, use them as defense-in-depth, but do not rely on them for correctness.

### Plaintext rendering
Use a dedicated plaintext control/view model with:
- preserved whitespace
- wrapping toggle if needed later
- copy/select behavior
- no HTML interpretation

## Settings and preferences
Add mail-specific settings keys for:
- last active main view = `mail`
- selected mailbox / account group
- left pane width
- thread list width
- optional sync retention window
- default mail body mode (`Auto`, `Plain text`, `HTML`) if desired

Do **not** add a global `always load remote resources` setting in the first cut.

## Testing strategy

### Unit tests
- HTML sanitizer strips scripts, event handlers, forms, iframes, remote CSS, and unsafe URLs
- inline CID rewriting resolves correctly
- external-resource detection and per-message approval logic works
- Gmail/JMAP DTO mapping to provider-agnostic mail models
- thread aggregation logic from message deltas
- account migration from old account types to provider/capability model

### Storage tests
- new migrations apply cleanly from existing schema
- mailbox/thread/message upserts are idempotent
- many-to-many mailbox membership behaves correctly
- sync cursors/history ids survive restarts

### UI tests
Mirror the current headless AtomUI tests:
- main window exposes a Mail segmented item/menu entry
- mail view uses AtomUI controls for search/filter/actions
- account wizard exposes mail-capability controls with AtomUI components
- preview surface exposes plaintext/HTML toggle and external-resource button states

### Integration/provider tests
- sample JMAP session/mailbox/email payloads
- sample Gmail label/message/history payloads
- incremental sync replay
- body hydration + attachment download paths

## Recommended implementation phases

### Phase 1: account and schema foundation
1. Introduce provider kind + capability model.
2. Add migrations for account backfill and mail tables.
3. Update `SqliteStorage` CRUD/query methods for the new account semantics and mail entities.
4. Keep calendar/contact behavior green before adding mail features.

### Phase 2: provider and sync foundation
1. Add `IMailProvider`, provider DTOs, and `MailSyncService`.
2. Implement Gmail mailbox/message metadata sync.
3. Implement JMAP mailbox/message metadata sync.
4. Add progress messages and integrate into existing sync status plumbing.

### Phase 3: mail UI skeleton
1. Add `Mail` shell mode and persist last-active state.
2. Build mailbox tree, thread list, and preview panes with AtomUI controls.
3. Wire local-query-backed view models.
4. Support mailbox selection, thread selection, refresh, and empty states.

### Phase 4: message hydration and secure rendering
1. Add lazy body hydration on selection.
2. Implement plaintext preview.
3. Implement secure HTML preview via Avalonia WebView.
4. Add per-message `Load external resources` flow.
5. Add attachment metadata list and download/open actions.

### Phase 5: mail actions
Initial confirmed scope includes message-state mutation:
1. mark read/unread
2. star/unstar
3. archive/move/delete
4. keep compose/reply/reply-all/forward out of the first delivery

### Phase 6: polish and verification
1. headless UI tests for AtomUI usage
2. migration tests
3. provider fixture coverage
4. manual verification on Linux with WebView runtime available
5. packaging/runtime notes for Avalonia WebView prerequisites

## Risks and mitigations
- **Risk:** adding mail as new `AccountType` values duplicates Google identities and creates long-term settings debt.
  - **Mitigation:** provider/capability split first.
- **Risk:** full-message sync explodes storage and slows startup.
  - **Mitigation:** metadata-first sync; hydrate bodies/attachments lazily.
- **Risk:** WebView security differs by platform.
  - **Mitigation:** sanitize + local URI rewrite + explicit request allow-list; platform toggles only as defense-in-depth.
- **Risk:** Gmail and JMAP mailbox semantics differ (labels vs folders, thread behavior).
  - **Mitigation:** normalize to mailbox membership + local thread summary model.
- **Risk:** Linux WebView runtime availability.
  - **Mitigation:** document WPE/WebKit prerequisites and detect unsupported runtime early.

## Confirmed product decisions
- initial scope: **read plus message state changes** (`read/unread`, `star`, `archive/delete`)
- Google account model: **reuse existing Google accounts and expand scopes/capabilities**
- JMAP auth for first cut: **username/password or app-password plus bearer token**
- offline retention: **recent metadata with on-demand body fetch/cache**
- threading model: **conversation/thread-first UI**
- remote resources: **disabled by default, in-memory per-message approval only**

## Implementation todo list
- [x] Replace account-type-only model with provider kind + capabilities
- [x] Add SQLite migrations for account backfill and mail tables
- [x] Extend `SqliteStorage` with mail CRUD/query/sync-state methods
- [x] Add mail domain models (`Mailbox`, `MailThread`, `MailMessage`, `MailBody`, `MailAttachment`)
- [x] Add `IMailProvider` and provider DTOs
- [x] Implement Gmail mail service/provider
- [x] Implement JMAP mail service/provider
- [x] Add `MailSyncService` and progress messages
- [x] Add Mail main-window mode and persisted main-view state
- [x] Build mailbox tree/thread list/preview AtomUI views and view models
- [x] Implement lazy message hydration
- [x] Implement secure HTML sanitization/rewrite pipeline
- [x] Implement `NativeWebView` wrapper with per-message external-resource approval
- [x] Add plaintext renderer
- [x] Add attachment metadata/download UX
- [x] Add confirmed message-state mutation actions (`read/unread`, `star`, `archive/delete`)
- [x] Add migration/unit/UI tests for the delivered mail feature surface
- [ ] Verify Linux WebView prerequisites and runtime behavior on a machine with `wpewebkit` available

## External references used for planning
- Avalonia WebView docs: https://docs.avaloniaui.net/docs/app-development/embedding-web-content
- Avalonia NativeWebView API: https://docs.avaloniaui.net/controls/web/nativewebview
- JMAP Mail (RFC 8621): https://www.rfc-editor.org/rfc/rfc8621.html
- Gmail API reference: https://developers.google.com/workspace/gmail/api/reference/rest
