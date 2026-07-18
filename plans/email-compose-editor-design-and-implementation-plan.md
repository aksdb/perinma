# Email compose editor design and implementation plan

## Goal
Add a mail compose workflow to perinma that supports:
- new message
- save/edit drafts
- reply / reply-all / forward
- rich-text HTML authoring
- inline images and regular attachments
- selectable sender identities / aliases when the provider exposes them
- hybrid draft persistence: local autosave plus provider drafts when online

The compose feature MUST fit the existing Mail workspace, reuse the current provider/service/storage patterns where they still make sense, and remain safe and maintainable.

## Confirmed product decisions
- Compose scope: `new message + drafts + reply/reply-all/forward`
- From field: `selectable provider identities / send-as aliases when available`
- Draft persistence: `hybrid local autosave plus provider drafts when online`

## Current architecture observations
- The current mail stack is read/sync oriented:
  - `IMailProvider` exposes mailbox sync, message hydration, attachment download, and message-state mutations.
  - `MailSyncService` orchestrates provider sync and local projection into SQLite.
- Existing mail storage (`mailbox`, `mail_thread`, `mail_message`, `mail_attachment`) models synced provider data well, but it is not a good canonical store for an actively edited draft.
- The app already uses `Avalonia.Controls.WebView` / `NativeWebView` safely for HTML preview in `src/Views/Mail/SecureMailHtmlView.*`.
- `NativeWebView` supports JS↔C# messaging (`WebMessageReceived`) and host-initiated JS execution (`InvokeScript`), which is enough to host a local rich-text editor surface.
- The app already uses separate windows/dialogs with static `ShowAsync(...)` helpers, e.g. `ContactEditDialog`, which is a good fit for compose.
- Google mail auth currently uses `gmail.modify`; official Gmail docs state that scope is sufficient for compose/send operations and also for listing send-as aliases.
- Current JMAP support only discovers mail-read capabilities and `downloadUrl`; compose/send will require JMAP submission/identity/upload support to be added explicitly.

## Recommended architectural direction

### 1. UI shell: separate compose window, not inline editing in the preview pane
Use a separate `ComposeMailWindow` (owned by the main window, but modeless) instead of reusing the preview pane.

Why:
- compose is a task flow, not a preview state
- users must be able to browse mail while a draft stays open
- reply / forward / draft resume become much simpler
- it avoids overloading `MailViewModel` with editor lifecycle, autosave, file-picking, and send state

Recommended UI entry points:
- top toolbar button: `Compose`
- message-level actions: `Reply`, `Reply all`, `Forward`
- draft open action from Drafts mailbox / local drafts list

Recommended window layout:
- From selector
- To / Cc / Bcc recipients
- Subject
- rich editor toolbar
- editor surface
- attachments strip/list
- status row (`Saved locally`, `Saved to provider`, `Offline`, `Sending...`)
- actions: `Send`, `Save draft`, `Discard`

## 2. Split sync/read from compose/send
Do **not** continue bloating `IMailProvider` with compose-specific concerns.

Recommended split:
- keep `IMailProvider` for read/sync/open/download/message-state mutation
- add a new compose-capable contract, e.g. `IMailComposeProvider`

Recommended provider compose contract:
```csharp
public interface IMailComposeProvider
{
    Task<MailComposeCapabilities> GetComposeCapabilitiesAsync(string accountId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MailIdentity>> GetSenderIdentitiesAsync(string accountId, CancellationToken cancellationToken = default);
    Task<ProviderDraftReference> SaveDraftAsync(string accountId, ProviderComposedMessage message, ProviderDraftReference? existingDraft, CancellationToken cancellationToken = default);
    Task DeleteDraftAsync(string accountId, ProviderDraftReference draft, CancellationToken cancellationToken = default);
    Task<ProviderSendResult> SendAsync(string accountId, ProviderComposedMessage message, ProviderDraftReference? existingDraft, CancellationToken cancellationToken = default);
}
```

This split matters because compose capability is not guaranteed for every mail provider/account. In particular, a JMAP account may support `mail` but not `submission`.

### 3. Introduce a dedicated local compose model
Use a separate local draft model rather than overloading synced `mail_message` rows.

Recommended new local model:
- `MailComposeDraft`
  - local draft id
  - account id
  - provider type
  - compose kind: `New`, `Reply`, `ReplyAll`, `Forward`
  - remote draft reference JSON (provider-specific)
  - remote thread/message linkage for reply/forward
  - selected from identity id
  - subject
  - sanitized HTML body snapshot
  - plain-text alternative snapshot
  - autosave timestamps (`last_local_save`, `last_remote_save`)
  - sync status (`LocalOnly`, `PendingRemoteSave`, `Synced`, `Conflict`, `SendFailed`)
  - provider ETag/state token / raw metadata JSON
- `MailComposeRecipient`
  - draft id
  - recipient kind: `To`, `Cc`, `Bcc`
  - display name
  - address
  - sort order
- `MailComposeAttachment`
  - draft id
  - local attachment id
  - original file name
  - MIME type
  - size
  - staged local file path
  - `IsInline`
  - `ContentId`
  - hash / dedupe key
  - optional provider blob reference JSON

Recommended storage path for staged files:
- app data directory, e.g. `mail-compose/<draftId>/...`
- keep compose staging separate from synced message attachment cache

### 4. Editor architecture: local web editor inside `NativeWebView`
A true rich-text mail editor should be hosted inside `NativeWebView` with a local asset bundle.

Do **not** try to build this from native Avalonia text controls.
Do **not** use deprecated `execCommand` as the core editing primitive.
Do **not** load editor JS/CSS from a CDN.

Recommended editor design:
- add `ComposeMailEditorView` wrapping `NativeWebView`
- load a local editor app from `src/Assets/MailComposeEditor/`
- use JS↔C# messages for:
  - document changed
  - toolbar state changed
  - paste/drop image detected
  - link insertion/editing
  - selection changes
- use C#→JS calls for:
  - set initial HTML
  - insert inline image placeholder
  - toggle bold/italic/etc.
  - request plain-text export
  - focus editor

Recommended authoring format:
- editor maintains a constrained HTML subset suitable for email
- C# stores the latest sanitized HTML snapshot plus derived plain-text body
- outgoing HTML is re-sanitized before remote save/send; never trust the editor DOM blindly

Recommended v1 formatting subset:
- paragraphs / line breaks
- bold / italic / underline / strike
- ordered/unordered lists
- blockquote
- code / preformatted text
- hyperlinks
- inline images

Recommended v1 omission:
- no explicit “insert remote image URL” UI
- images should be uploaded/pasted and embedded inline as CID-related parts

That keeps sending predictable and avoids turning the editor into a remote-content authoring surface.

### 5. Outgoing HTML pipeline
Add a dedicated compose sanitization and export pipeline rather than reusing the preview sanitizer as-is.

Recommended steps:
1. receive HTML snapshot from editor
2. sanitize to allowed outbound subset (`MailComposeHtmlSanitizer`)
3. normalize markup (paragraph/list structure, anchors, image references)
4. derive plain-text alternative from sanitized DOM
5. resolve inline image placeholders to stable `cid:` references
6. build provider-specific payload from the normalized compose model

Important separation:
- preview sanitizer protects the app from inbound hostile mail
- compose sanitizer protects outbound consistency and provider interoperability

They serve different purposes and SHOULD remain separate components.

### 6. Message assembly layer
Add a provider-neutral `MailComposerService` that turns a local compose draft into a normalized sendable message model.

Recommended internal model:
- `ComposedEnvelope`
  - from identity
  - to / cc / bcc
  - subject
  - reply headers (`In-Reply-To`, `References`)
- `ComposedBody`
  - sanitized HTML
  - plain text
- `ComposedAttachment`
  - regular attachments
  - inline image attachments with CID mapping

Responsibilities:
- reply / reply-all recipient calculation
- identity-aware self-address filtering
- forward header block generation
- attachment classification
- MIME generation for Gmail
- provider payload shaping for JMAP

### 7. Gmail implementation
Recommended Gmail implementation:
- fetch sender identities via `users.settings.sendAs.list`
- create/update drafts with `users.drafts.create` / `users.drafts.update`
- send with `users.drafts.send` when a draft exists, otherwise `users.messages.send`
- build RFC 2822 MIME using:
  - `multipart/alternative` for plain + HTML
  - `multipart/related` for inline CID images
  - `multipart/mixed` for normal attachments
- store both Gmail draft id and contained message id in the provider draft reference
- preserve threading headers (`In-Reply-To`, `References`) and use Gmail thread id when available

Recommended detail:
- continue using `gmail.modify` as the Google mail scope for this feature
- do not introduce broader scopes unless the implementation proves it is strictly necessary

### 8. JMAP implementation
Recommended JMAP implementation:
- extend session discovery to capture:
  - `urn:ietf:params:jmap:submission`
  - `uploadUrl`
  - `downloadUrl`
  - `Identity/get`
- if `submission` capability is absent, disable compose/send for that account in the UI
- fetch sender identities via `Identity/get`
- upload attachments / inline images via `uploadUrl`
- save drafts using `Email/set` with `$draft` keyword and the provider’s Drafts mailbox
- send using `EmailSubmission/set`
- use `onSuccessUpdateEmail` / `onSuccessDestroyEmail` so draft/send transitions are reflected cleanly
- map inline images with `blobId` + `cid`

Recommended draft reference fields for JMAP:
- `emailId`
- `threadId`
- `identityId`
- `mailboxId`
- provider state token / last known session state

### 9. Draft sync model
For the selected hybrid strategy:
- local autosave is authoritative for crash recovery and offline continuity
- provider draft save is the cross-device / server-visible projection
- saves should be debounced, not immediate on every keystroke

Recommended policy:
- local save debounce: short
- provider save debounce: longer, and only when online
- send MUST flush pending local/editor state before calling provider send

Conflict handling recommendation:
- if remote draft changed since the last provider save, do not merge silently
- mark the draft `Conflict`
- preserve the local draft
- offer explicit user actions later (`Keep local`, `Reload remote`, `Duplicate`)

### 10. Reply / reply-all / forward behavior
Recommended rules:
- `Reply`: target `Reply-To` if present, otherwise sender/from
- `Reply all`: include original `To`/`Cc` minus any of the user’s known identities/aliases, de-duped case-insensitively
- `Forward`: create a new compose draft containing a generated forwarded-header block and copy attachments by default
- quote source content using both:
  - sanitized HTML blockquote form
  - plain-text quoted alternative

### 11. Mail view integration
Recommended Mail workspace changes:
- add `Compose` button to the mail toolbar
- add per-message actions for `Reply`, `Reply all`, `Forward`
- allow opening synced Draft messages into the compose window
- optionally add a lightweight `Local Drafts` section if a draft exists locally but has not yet been saved remotely

The read preview and compose surfaces should stay separate.

### 12. Security and correctness rules
- never execute editor assets from the network
- never trust HTML received back from the web editor without sanitizing it again in C#
- never reuse inbound remote-resource permission state for authored mail
- keep provider-specific ids/state out of the editor; they belong in the compose VM/service layer
- keep attachment staging files scoped per draft and delete them on discard/send cleanup
- ensure send is impossible without a valid selected identity and at least one recipient

### 13. Testing strategy
Recommended test layers:
- unit tests
  - compose sanitizer
  - plain-text derivation
  - reply-all recipient calculation
  - forward model generation
  - Gmail MIME assembly
  - JMAP draft payload assembly
  - inline image CID mapping
- storage tests
  - local draft autosave/load
  - attachment staging persistence
  - conflict state transitions
- UI tests
  - compose window opens from toolbar/actions
  - reply/reply-all/forward prefill
  - editor bridge updates VM state
  - autosave indicators and offline state
- provider tests
  - Gmail draft create/update/send behavior
  - Gmail alias loading
  - JMAP identity loading
  - JMAP submission capability gating
  - JMAP upload + draft + send flow

## Recommended implementation phases

### Phase 1: foundations
1. Add compose domain models and local draft storage.
2. Add `MailComposerService` and compose sanitizer/export pipeline.
3. Add provider compose interfaces/capabilities.

### Phase 2: editor and compose window
4. Add `ComposeMailWindow`, `ComposeMailViewModel`, `ComposeMailEditorView`.
5. Host the local rich-text editor bundle in `NativeWebView`.
6. Add toolbar commands, recipient editing, attachment staging, status row.

### Phase 3: provider support
7. Implement Gmail identities + draft create/update/send.
8. Extend JMAP session discovery for `uploadUrl`, identities, and submission capability.
9. Implement JMAP draft save/send.

### Phase 4: workflow integration
10. Add compose/reply/reply-all/forward entry points in `MailView`.
11. Open synced drafts into the compose window.
12. Refresh local/synced mail state after save/send/discard.

### Phase 5: hardening
13. Add conflict detection and recovery flows.
14. Add cleanup for discarded/sent draft staging files.
15. Add targeted regression coverage across Gmail + JMAP compose paths.

## Non-goals for this feature
- scheduled send
- signatures and signature-settings editing
- S/MIME / encryption / signing
- markdown mode
- collaborative draft editing
- full WYSIWYG fidelity with every pasted HTML email template on day one

## Risks to plan for
- A web editor inside `NativeWebView` is the right tool here, but it adds an asset-bundle and JS-bridge boundary that must be tested explicitly.
- Gmail and JMAP draft models are not structurally identical; a provider-neutral local draft plus provider-specific projection is required.
- JMAP compose availability is capability-dependent; read support does not imply send support.
- Inline images require careful lifecycle management so local staged files, provider blob ids, and exported `cid:` values stay aligned.

## Todo checklist
- [x] Confirm compose scope: new + drafts + reply/reply-all/forward
- [x] Confirm sender identity behavior: selectable identities / aliases when available
- [x] Confirm draft persistence behavior: hybrid local autosave + provider drafts when online
- [x] Review existing mail architecture, storage, and provider extension points
- [x] Add compose domain and storage models
- [x] Add `IMailComposeProvider` and compose capability/identity contracts
- [x] Add local draft repository/storage APIs and migration
- [x] Add attachment staging service for compose drafts
- [x] Add compose HTML sanitizer + plain-text exporter
- [x] Add `MailComposerService` and provider-neutral compose model
- [x] Add `ComposeMailWindow`, `ComposeMailViewModel`, and `ComposeMailEditorView`
- [x] Add local editor asset bundle and JS bridge
- [x] Add Gmail alias loading + draft create/update/send support
- [x] Add JMAP identity discovery + upload + draft save/send support
- [x] Add compose/reply/reply-all/forward actions in `MailView`
- [x] Add draft conflict detection and recovery UI
- [x] Add cleanup for staged draft attachments on discard/send
- [x] Add targeted unit/storage/UI/provider tests
- [x] Run broader solution regression suite

## References
- Avalonia `NativeWebView` docs: https://docs.avaloniaui.net/controls/web/nativewebview
- Gmail draft guide: https://developers.google.com/workspace/gmail/api/guides/drafts
- Gmail sending guide: https://developers.google.com/workspace/gmail/api/guides/sending
- Gmail uploads guide: https://developers.google.com/workspace/gmail/api/guides/uploads
- Gmail scopes guide: https://developers.google.com/workspace/gmail/api/auth/scopes
- Gmail send-as aliases: https://developers.google.com/workspace/gmail/api/reference/rest/v1/users.settings.sendAs/list
- JMAP core (session/upload/download): https://www.rfc-editor.org/rfc/rfc8620
- JMAP mail + identities + submission: https://www.rfc-editor.org/rfc/rfc8621
