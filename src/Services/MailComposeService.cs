using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using perinma.Models;
using perinma.Storage;
using perinma.Storage.Models;

namespace perinma.Services;

public sealed class MailComposeService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly SqliteStorage _storage;
    private readonly MailComposeAttachmentService _attachmentService;
    private readonly MailComposerService _composerService;
    private readonly IReadOnlyDictionary<AccountType, IMailComposeProvider> _composeProviders;
    private readonly IReadOnlyDictionary<AccountType, IMailProvider> _mailProviders;

    public MailComposeService(
        SqliteStorage storage,
        MailComposeAttachmentService attachmentService,
        MailComposerService composerService,
        IReadOnlyDictionary<AccountType, IMailComposeProvider> composeProviders,
        IReadOnlyDictionary<AccountType, IMailProvider> mailProviders)
    {
        _storage = storage;
        _attachmentService = attachmentService;
        _composerService = composerService;
        _composeProviders = composeProviders;
        _mailProviders = mailProviders;
    }

    public async Task<MailComposeDraft> CreateDraftAsync(
        string accountId,
        MailComposeKind kind,
        MailComposeSourceMessage? source = null,
        CancellationToken cancellationToken = default)
    {
        var account = await GetAccountAsync(accountId);
        var identities = await GetSenderIdentitiesAsync(accountId, cancellationToken);
        var draft = _composerService.CreateDraft(Guid.Parse(accountId), account.AccountTypeEnum, kind, identities, source);
        if (kind == MailComposeKind.Forward && source != null)
            draft.Attachments = await ImportSourceAttachmentsAsync(draft, source, includeInline: false, cancellationToken);


        await SaveLocalDraftAsync(draft, cancellationToken);
        return draft;
    }

    public async Task<MailComposeDraft?> GetDraftAsync(string draftId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var draft = await _storage.GetMailComposeDraftByIdAsync(draftId);
        if (draft == null)
            return null;

        var recipients = (await _storage.GetMailComposeRecipientsAsync(draftId)).ToList();
        var attachments = (await _storage.GetMailComposeAttachmentsAsync(draftId)).ToList();
        return new MailComposeDraft
        {
            Id = Guid.Parse(draft.DraftId),
            AccountId = Guid.Parse(draft.AccountId),
            Kind = draft.ComposeKindEnum,
            SourceMessageId = ParseNullableGuid(draft.SourceMessageId),
            SourceMessageExternalId = draft.SourceMessageExternalId,
            SourceThreadId = ParseNullableGuid(draft.SourceThreadId),
            SourceThreadExternalId = draft.SourceThreadExternalId,
            SourceInternetMessageId = draft.SourceInternetMessageId,
            RemoteDraftReferenceJson = draft.RemoteDraftReferenceJson,
            SelectedIdentityId = draft.SelectedIdentityId,
            SelectedIdentityDisplayName = draft.SelectedIdentityDisplayName,
            SelectedIdentityAddress = draft.SelectedIdentityAddress,
            Subject = draft.Subject,
            ToText = await _storage.GetMailComposeDraftDataAsync(draftId, "toText") ?? string.Empty,
            CcText = await _storage.GetMailComposeDraftDataAsync(draftId, "ccText") ?? string.Empty,
            BccText = await _storage.GetMailComposeDraftDataAsync(draftId, "bccText") ?? string.Empty,
            HtmlBody = draft.HtmlBody,
            PlainTextBody = draft.PlainTextBody,
            Status = draft.StatusEnum,
            UpdatedAt = DateTimeOffset.FromUnixTimeSeconds(draft.UpdatedAt),
            LastLocalSaveAt = draft.LastLocalSaveAt.HasValue ? DateTimeOffset.FromUnixTimeSeconds(draft.LastLocalSaveAt.Value) : null,
            LastRemoteSaveAt = draft.LastRemoteSaveAt.HasValue ? DateTimeOffset.FromUnixTimeSeconds(draft.LastRemoteSaveAt.Value) : null,
            Recipients = recipients.Select(MapRecipient).ToList(),
            Attachments = attachments.Select(MapAttachment).ToList()
        };
    }

    public async Task<IReadOnlyList<MailComposeDraft>> GetDraftsAsync(string? accountId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var drafts = await _storage.GetMailComposeDraftsAsync(accountId);
        var results = new List<MailComposeDraft>();
        foreach (var draft in drafts)
        {
            var loadedDraft = await GetDraftAsync(draft.DraftId, cancellationToken);
            if (loadedDraft != null)
                results.Add(loadedDraft);
        }

        return results;
    }

    public async Task<MailComposeCapabilities> GetComposeCapabilitiesAsync(string accountId, CancellationToken cancellationToken = default)
    {
        var account = await GetAccountAsync(accountId);
        return _composeProviders.TryGetValue(account.AccountTypeEnum, out var provider)
            ? await provider.GetComposeCapabilitiesAsync(accountId, cancellationToken)
            : new MailComposeCapabilities();
    }

    public async Task<IReadOnlyList<MailIdentity>> GetSenderIdentitiesAsync(string accountId, CancellationToken cancellationToken = default)
    {
        var account = await GetAccountAsync(accountId);
        if (!_composeProviders.TryGetValue(account.AccountTypeEnum, out var provider))
            return [];

        return await provider.GetSenderIdentitiesAsync(accountId, cancellationToken);
    }

    public Task SaveLocalDraftAsync(MailComposeDraft draft, CancellationToken cancellationToken = default)
        => PersistDraftAsync(draft, updateLocalSaveTimestamp: true, cancellationToken);

    public async Task<MailComposeAttachment> StageAttachmentAsync(
        MailComposeDraft draft,
        string sourcePath,
        bool isInline = false,
        CancellationToken cancellationToken = default)
    {
        var attachment = await _attachmentService.StageFileAsync(
            draft.Id.ToString(),
            sourcePath,
            isInline,
            sortOrder: draft.Attachments.Count,
            cancellationToken: cancellationToken);
        draft.Attachments.Add(attachment);
        return attachment;
    }

    public async Task<MailComposeAttachment> StageAttachmentBytesAsync(
        MailComposeDraft draft,
        string fileName,
        string mimeType,
        byte[] content,
        bool isInline = false,
        CancellationToken cancellationToken = default)
    {
        var attachment = await _attachmentService.StageBytesAsync(
            draft.Id.ToString(),
            fileName,
            mimeType,
            content,
            isInline,
            sortOrder: draft.Attachments.Count,
            cancellationToken: cancellationToken);
        draft.Attachments.Add(attachment);
        return attachment;
    }

    public Task RemoveAttachmentAsync(MailComposeDraft draft, Guid attachmentId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var attachment = draft.Attachments.FirstOrDefault(candidate => candidate.Id == attachmentId);
        if (attachment == null)
            return Task.CompletedTask;

        if (!string.IsNullOrWhiteSpace(attachment.ContentPath) && File.Exists(attachment.ContentPath))
            File.Delete(attachment.ContentPath);

        draft.Attachments.Remove(attachment);
        for (var index = 0; index < draft.Attachments.Count; index++)
            draft.Attachments[index].SortOrder = index;
        return Task.CompletedTask;
    }

    public Task<List<MailComposeAttachment>> ImportSourceAttachmentsAsync(
        MailComposeDraft draft,
        MailComposeSourceMessage source,
        bool includeInline,
        CancellationToken cancellationToken = default)
        => PrepareSourceAttachmentsAsync(draft.Id.ToString(), source, includeInline, cancellationToken);


    public async Task SaveRemoteDraftAsync(MailComposeDraft draft, CancellationToken cancellationToken = default)
    {
        var account = await GetAccountAsync(draft.AccountId.ToString());
        var provider = GetComposeProvider(account.AccountTypeEnum);
        var capabilities = await provider.GetComposeCapabilitiesAsync(account.AccountId, cancellationToken);
        if (!capabilities.SupportsDrafts || !capabilities.SupportsRemoteDrafts)
            throw new InvalidOperationException($"Provider '{account.AccountTypeEnum}' does not support remote drafts.");

        try
        {
            await PersistDraftAsync(draft, updateLocalSaveTimestamp: true, cancellationToken);
            var composedMessage = _composerService.BuildProviderMessage(draft);
            var remoteDraft = await provider.SaveDraftAsync(
                account.AccountId,
                composedMessage,
                DeserializeDraftReference(draft.RemoteDraftReferenceJson),
                cancellationToken);

            draft.RemoteDraftReferenceJson = JsonSerializer.Serialize(remoteDraft, JsonOptions);
            draft.Status = MailComposeDraftStatus.Synced;
            draft.LastRemoteSaveAt = DateTimeOffset.UtcNow;
            await PersistDraftAsync(draft, updateLocalSaveTimestamp: false, cancellationToken);
        }
        catch (InvalidOperationException ex) when (!string.IsNullOrWhiteSpace(draft.RemoteDraftReferenceJson) && IsRemoteConflict(ex))
        {
            draft.Status = MailComposeDraftStatus.Conflict;
            await PersistDraftAsync(draft, updateLocalSaveTimestamp: false, cancellationToken);
            throw new MailComposeConflictException("Remote draft changed. Local draft was kept; use 'Save as New' to create a fresh remote draft.", ex);
        }
    }

    public async Task SaveAsNewRemoteDraftAsync(MailComposeDraft draft, CancellationToken cancellationToken = default)
    {
        draft.RemoteDraftReferenceJson = null;
        draft.Status = MailComposeDraftStatus.LocalOnly;
        await SaveRemoteDraftAsync(draft, cancellationToken);
    }

    public async Task<ProviderSendResult> SendAsync(MailComposeDraft draft, CancellationToken cancellationToken = default)
    {
        var account = await GetAccountAsync(draft.AccountId.ToString());
        var provider = GetComposeProvider(account.AccountTypeEnum);
        var capabilities = await provider.GetComposeCapabilitiesAsync(account.AccountId, cancellationToken);
        if (!capabilities.SupportsSend)
            throw new InvalidOperationException($"Provider '{account.AccountTypeEnum}' does not support sending mail.");

        await PersistDraftAsync(draft, updateLocalSaveTimestamp: true, cancellationToken);
        var result = await provider.SendAsync(
            account.AccountId,
            _composerService.BuildProviderMessage(draft),
            DeserializeDraftReference(draft.RemoteDraftReferenceJson),
            cancellationToken);

        await _storage.DeleteMailComposeDraftAsync(draft.Id.ToString());
        await _attachmentService.DeleteDraftFilesAsync(draft.Id.ToString(), cancellationToken);
        return result;
    }

    public async Task DiscardDraftAsync(MailComposeDraft draft, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(draft.RemoteDraftReferenceJson))
        {
            var account = await GetAccountAsync(draft.AccountId.ToString());
            if (_composeProviders.TryGetValue(account.AccountTypeEnum, out var provider))
            {
                var capabilities = await provider.GetComposeCapabilitiesAsync(account.AccountId, cancellationToken);
                if (capabilities.SupportsRemoteDrafts)
                {
                    var draftReference = DeserializeDraftReference(draft.RemoteDraftReferenceJson);
                    if (draftReference != null)
                        await provider.DeleteDraftAsync(account.AccountId, draftReference, cancellationToken);
                }
            }
        }

        await _storage.DeleteMailComposeDraftAsync(draft.Id.ToString());
        await _attachmentService.DeleteDraftFilesAsync(draft.Id.ToString(), cancellationToken);
    }

    private async Task PersistDraftAsync(MailComposeDraft draft, bool updateLocalSaveTimestamp, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sanitizedBody = MailComposeHtmlSanitizer.Sanitize(draft.HtmlBody, allowLocalFileReferences: true);
        draft.HtmlBody = sanitizedBody.Html;
        draft.PlainTextBody = string.IsNullOrWhiteSpace(sanitizedBody.PlainText) ? draft.PlainTextBody : sanitizedBody.PlainText;

        var now = DateTimeOffset.UtcNow;
        if (updateLocalSaveTimestamp)
        {
            draft.LastLocalSaveAt = now;
            if (!string.IsNullOrWhiteSpace(draft.RemoteDraftReferenceJson) && draft.Status == MailComposeDraftStatus.Synced)
                draft.Status = MailComposeDraftStatus.PendingRemoteSave;
        }

        draft.UpdatedAt = now;
        var draftId = await _storage.CreateOrUpdateMailComposeDraftAsync(
            MapDraft(draft),
            BuildRecipientRows(draft),
            draft.Attachments.Select(MapAttachment).ToList());

        await _storage.SetMailComposeDraftDataAsync(draftId, "toText", draft.ToText ?? string.Empty);
        await _storage.SetMailComposeDraftDataAsync(draftId, "ccText", draft.CcText ?? string.Empty);
        await _storage.SetMailComposeDraftDataAsync(draftId, "bccText", draft.BccText ?? string.Empty);
    }

    private async Task<List<MailComposeAttachment>> PrepareSourceAttachmentsAsync(
        string draftId,
        MailComposeSourceMessage source,
        bool includeInline,
        CancellationToken cancellationToken)
    {
        var attachments = new List<MailComposeAttachment>();
        if (!_mailProviders.TryGetValue(source.AccountType, out var provider))
            return attachments;

        var sortOrder = 0;
        foreach (var sourceAttachment in source.Attachments.Where(attachment => includeInline || !attachment.IsInline))
        {
            MailComposeAttachment? stagedAttachment = null;
            if (!string.IsNullOrWhiteSpace(sourceAttachment.ContentPath) && File.Exists(sourceAttachment.ContentPath))
            {
                stagedAttachment = await _attachmentService.StageFileAsync(
                    draftId,
                    sourceAttachment.ContentPath,
                    sourceAttachment.IsInline,
                    sourceAttachment.ContentId,
                    sortOrder,
                    cancellationToken);

            }
            else if (!string.IsNullOrWhiteSpace(source.MessageExternalId)
                     && !string.IsNullOrWhiteSpace(sourceAttachment.ExternalId))
            {
                var downloadedAttachment = await provider.DownloadAttachmentAsync(
                    source.AccountId.ToString(),
                    source.MessageExternalId,
                    sourceAttachment.ExternalId,
                    cancellationToken);
                stagedAttachment = await _attachmentService.StageBytesAsync(
                    draftId,
                    string.IsNullOrWhiteSpace(downloadedAttachment.FileName) ? sourceAttachment.FileName : downloadedAttachment.FileName,
                    string.IsNullOrWhiteSpace(downloadedAttachment.MimeType) ? sourceAttachment.MimeType : downloadedAttachment.MimeType,
                    downloadedAttachment.Content,
                    sourceAttachment.IsInline,
                    sourceAttachment.ContentId,
                    sortOrder,
                    cancellationToken);
            }

            if (stagedAttachment != null)
            {
                stagedAttachment.SortOrder = sortOrder;
                attachments.Add(stagedAttachment);
                sortOrder++;
            }
        }

        return attachments;
    }

    private async Task<AccountDbo> GetAccountAsync(string accountId)
        => await _storage.GetAccountByIdAsync(accountId)
           ?? throw new InvalidOperationException($"Account '{accountId}' was not found.");

    private IMailComposeProvider GetComposeProvider(AccountType accountType)
        => _composeProviders.TryGetValue(accountType, out var provider)
            ? provider
            : throw new InvalidOperationException($"No compose provider registered for account type '{accountType}'.");

    private IEnumerable<MailComposeRecipientDbo> BuildRecipientRows(MailComposeDraft draft)
        => BuildRecipientRows(draft.Id.ToString(), MailRecipientKind.To, draft.ToText)
            .Concat(BuildRecipientRows(draft.Id.ToString(), MailRecipientKind.Cc, draft.CcText))
            .Concat(BuildRecipientRows(draft.Id.ToString(), MailRecipientKind.Bcc, draft.BccText));

    private static IEnumerable<MailComposeRecipientDbo> BuildRecipientRows(string draftId, MailRecipientKind kind, string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return [];

        var rows = new List<MailComposeRecipientDbo>();
        var parts = rawText.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < parts.Length; index++)
        {
            try
            {
                var parsedAddress = new System.Net.Mail.MailAddress(parts[index]);
                rows.Add(new MailComposeRecipientDbo
                {
                    DraftId = draftId,
                    RecipientId = Guid.NewGuid().ToString(),
                    RecipientKind = kind.ToString(),
                    DisplayName = parsedAddress.DisplayName,
                    Address = parsedAddress.Address,
                    SortOrder = index
                });
            }
            catch (FormatException)
            {
                continue;
            }
        }

        return rows;
    }

    private static MailComposeDraftDbo MapDraft(MailComposeDraft draft)
    {
        return new MailComposeDraftDbo
        {
            DraftId = draft.Id.ToString(),
            AccountId = draft.AccountId.ToString(),
            ComposeKind = draft.Kind.ToString(),
            SourceMessageId = draft.SourceMessageId?.ToString(),
            SourceMessageExternalId = draft.SourceMessageExternalId,
            SourceThreadId = draft.SourceThreadId?.ToString(),
            SourceThreadExternalId = draft.SourceThreadExternalId,
            SourceInternetMessageId = draft.SourceInternetMessageId,
            RemoteDraftReferenceJson = draft.RemoteDraftReferenceJson,
            SelectedIdentityId = draft.SelectedIdentityId,
            SelectedIdentityDisplayName = draft.SelectedIdentityDisplayName,
            SelectedIdentityAddress = draft.SelectedIdentityAddress,
            Subject = draft.Subject,
            HtmlBody = draft.HtmlBody,
            PlainTextBody = draft.PlainTextBody,
            Status = draft.Status.ToString(),
            LastLocalSaveAt = draft.LastLocalSaveAt?.ToUnixTimeSeconds(),
            LastRemoteSaveAt = draft.LastRemoteSaveAt?.ToUnixTimeSeconds(),
            UpdatedAt = draft.UpdatedAt.ToUnixTimeSeconds()
        };
    }

    private static MailComposeRecipient MapRecipient(MailComposeRecipientDbo recipient)
    {
        return new MailComposeRecipient
        {
            Id = Guid.Parse(recipient.RecipientId),
            Kind = recipient.RecipientKindEnum,
            Name = recipient.DisplayName ?? string.Empty,
            Address = recipient.Address,
            SortOrder = recipient.SortOrder
        };
    }

    private static MailComposeAttachment MapAttachment(MailComposeAttachmentDbo attachment)
    {
        return new MailComposeAttachment
        {
            Id = Guid.Parse(attachment.AttachmentId),
            FileName = attachment.FileName,
            MimeType = attachment.MimeType,
            Size = attachment.Size,
            IsInline = attachment.Inline,
            ContentId = attachment.ContentId,
            ContentPath = attachment.StagedFilePath,
            Hash = attachment.ContentHash,
            ProviderReferenceJson = attachment.ProviderAttachmentReferenceJson,
            SortOrder = attachment.SortOrder
        };
    }

    private static MailComposeAttachmentDbo MapAttachment(MailComposeAttachment attachment)
    {
        return new MailComposeAttachmentDbo
        {
            DraftId = string.Empty,
            AttachmentId = attachment.Id.ToString(),
            FileName = attachment.FileName,
            MimeType = attachment.MimeType,
            Size = attachment.Size,
            IsInline = attachment.IsInline ? 1 : 0,
            ContentId = attachment.ContentId,
            StagedFilePath = attachment.ContentPath ?? string.Empty,
            ContentHash = attachment.Hash,
            ProviderAttachmentReferenceJson = attachment.ProviderReferenceJson,
            SortOrder = attachment.SortOrder
        };
    }

    private static ProviderDraftReference? DeserializeDraftReference(string? json)
        => string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<ProviderDraftReference>(json, JsonOptions);

    private static bool IsRemoteConflict(Exception exception)
        => exception.Message.Contains("state", StringComparison.OrdinalIgnoreCase)
           || exception.Message.Contains("conflict", StringComparison.OrdinalIgnoreCase);

    private static Guid? ParseNullableGuid(string? value)
        => Guid.TryParse(value, out var parsedValue) ? parsedValue : null;
}
