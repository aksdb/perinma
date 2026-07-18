using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using perinma.Models;
using perinma.Storage.Models;

namespace perinma.Services.Jmap;

public class JmapMailProvider(
    JmapMailService jmapMailService,
    CredentialManagerService credentialManager)
    : IMailProvider, IMailComposeProvider
{
    public CredentialManagerService CredentialManager => credentialManager;

    public async Task<MailboxSyncResult> GetMailboxesAsync(
        string accountId,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        var result = await jmapMailService.GetMailboxesAsync(GetCredentials(accountId), cancellationToken);
        return new MailboxSyncResult
        {
            Mailboxes = result.Mailboxes.Select(MapMailbox).ToList(),
            SyncToken = result.SyncToken
        };
    }

    public async Task<MailMessageSyncResult> GetMessagesAsync(
        string accountId,
        string mailboxExternalId,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        var result = await jmapMailService.GetMessageSummariesAsync(
            GetCredentials(accountId),
            mailboxExternalId,
            syncToken,
            cancellationToken);

        return new MailMessageSyncResult
        {
            Messages = result.Messages.Select(MapMessage).ToList(),
            SyncToken = result.SyncToken,
            MissingMessagesAreAuthoritative = result.MissingMessagesAreAuthoritative
        };
    }

    public async Task<HydratedMailMessage> HydrateMessageAsync(
        string accountId,
        string messageExternalId,
        CancellationToken cancellationToken = default)
    {
        var message = await jmapMailService.GetMessageAsync(
            GetCredentials(accountId),
            messageExternalId,
            fetchBodies: true,
            cancellationToken);

        return new HydratedMailMessage
        {
            Message = MapMessage(message)
        };
    }

    public async Task<DownloadedMailAttachment> DownloadAttachmentAsync(
        string accountId,
        string messageExternalId,
        string attachmentExternalId,
        CancellationToken cancellationToken = default)
    {
        var credentials = GetCredentials(accountId);
        var message = await jmapMailService.GetMessageAsync(
            credentials,
            messageExternalId,
            fetchBodies: false,
            cancellationToken);

        var attachment = message.Attachments.FirstOrDefault(candidate =>
            string.Equals(candidate.ExternalId, attachmentExternalId, StringComparison.Ordinal));
        if (attachment == null)
            throw new InvalidOperationException(
                $"Attachment '{attachmentExternalId}' was not found on message '{messageExternalId}'.");

        var content = await jmapMailService.DownloadBlobAsync(
            credentials,
            attachment.ExternalId,
            attachment.FileName,
            attachment.MimeType,
            cancellationToken);

        return new DownloadedMailAttachment
        {
            FileName = attachment.FileName ?? attachment.ExternalId,
            MimeType = attachment.MimeType ?? "application/octet-stream",
            Content = content
        };
    }

    public Task SetReadStateAsync(
        string accountId,
        string messageExternalId,
        bool isRead,
        CancellationToken cancellationToken = default) =>
        jmapMailService.SetReadStateAsync(GetCredentials(accountId), messageExternalId, isRead, cancellationToken);

    public Task SetStarredStateAsync(
        string accountId,
        string messageExternalId,
        bool isStarred,
        CancellationToken cancellationToken = default) =>
        jmapMailService.SetStarredStateAsync(GetCredentials(accountId), messageExternalId, isStarred, cancellationToken);

    public Task ArchiveMessageAsync(
        string accountId,
        string messageExternalId,
        CancellationToken cancellationToken = default) =>
        jmapMailService.ArchiveMessageAsync(GetCredentials(accountId), messageExternalId, cancellationToken);

    public Task DeleteMessageAsync(
        string accountId,
        string messageExternalId,
        CancellationToken cancellationToken = default) =>
        jmapMailService.DeleteMessageAsync(GetCredentials(accountId), messageExternalId, cancellationToken);

    public Task<bool> TestConnectionAsync(
        string accountId,
        CancellationToken cancellationToken = default) =>
        jmapMailService.TestConnectionAsync(GetCredentials(accountId), cancellationToken);

    public Task<MailComposeCapabilities> GetComposeCapabilitiesAsync(
        string accountId,
        CancellationToken cancellationToken = default) =>
        jmapMailService.GetComposeCapabilitiesAsync(GetCredentials(accountId), cancellationToken);

    public Task<IReadOnlyList<MailIdentity>> GetSenderIdentitiesAsync(
        string accountId,
        CancellationToken cancellationToken = default) =>
        jmapMailService.GetSenderIdentitiesAsync(GetCredentials(accountId), cancellationToken);

    public Task<ProviderDraftReference> SaveDraftAsync(
        string accountId,
        ProviderComposedMessage message,
        ProviderDraftReference? existingDraft = null,
        CancellationToken cancellationToken = default) =>
        jmapMailService.SaveDraftAsync(GetCredentials(accountId), message, existingDraft, cancellationToken);

    public Task DeleteDraftAsync(
        string accountId,
        ProviderDraftReference draft,
        CancellationToken cancellationToken = default) =>
        jmapMailService.DeleteDraftAsync(GetCredentials(accountId), draft, cancellationToken);

    public Task<ProviderSendResult> SendAsync(
        string accountId,
        ProviderComposedMessage message,
        ProviderDraftReference? existingDraft = null,
        CancellationToken cancellationToken = default) =>
        jmapMailService.SendAsync(GetCredentials(accountId), message, existingDraft, cancellationToken);

    private JmapCredentials GetCredentials(string accountId)
    {
        var credentials = credentialManager.GetJmapCredentials(accountId);
        if (credentials == null)
            throw new InvalidOperationException($"No JMAP credentials found for account {accountId}");

        return credentials;
    }

    private static ProviderMailbox MapMailbox(JmapMailbox mailbox)
    {
        return new ProviderMailbox
        {
            ExternalId = mailbox.ExternalId,
            ParentExternalId = mailbox.ParentExternalId,
            Name = mailbox.Name,
            Role = mailbox.Role,
            UnreadCount = mailbox.UnreadCount,
            TotalCount = mailbox.TotalCount,
            Enabled = mailbox.Enabled,
            Deleted = false,
            Data = BuildRawData(mailbox.RawDataJson)
        };
    }

    private static ProviderMailMessage MapMessage(JmapMailMessage message)
    {
        return new ProviderMailMessage
        {
            ExternalId = message.ExternalId,
            ThreadExternalId = message.ThreadExternalId,
            InternetMessageId = message.InternetMessageId,
            Subject = message.Subject,
            SenderName = message.SenderName,
            SenderAddress = message.SenderAddress,
            SentAtUnixTime = message.SentAtUnixTime,
            ReceivedAtUnixTime = message.ReceivedAtUnixTime,
            Preview = message.Preview,
            PlainTextBody = message.PlainTextBody,
            HtmlBody = message.HtmlBody,
            HasHtmlBody = message.HasHtmlBody,
            HasPlainTextBody = message.HasPlainTextBody,
            HasAttachments = message.HasAttachments,
            HasExternalResources = message.HasExternalResources,
            HasBlockedContent = message.HasBlockedContent,
            IsUnread = message.IsUnread,
            IsStarred = message.IsStarred,
            IsAnswered = message.IsAnswered,
            IsDraft = message.IsDraft,
            Deleted = false,
            MailboxExternalIds = message.MailboxExternalIds,
            To = message.To.Select(MapAddress).ToList(),
            Cc = message.Cc.Select(MapAddress).ToList(),
            Bcc = message.Bcc.Select(MapAddress).ToList(),
            ReplyTo = message.ReplyTo.Select(MapAddress).ToList(),
            Attachments = message.Attachments.Select(MapAttachment).ToList(),
            Data = BuildRawData(message.RawDataJson)
        };
    }

    private static ProviderMailAttachment MapAttachment(JmapMailAttachment attachment)
    {
        return new ProviderMailAttachment
        {
            ExternalId = attachment.ExternalId,
            FileName = attachment.FileName,
            MimeType = attachment.MimeType,
            Size = attachment.Size,
            IsInline = attachment.IsInline,
            ContentId = attachment.ContentId,
            Data = BuildRawData(attachment.RawDataJson)
        };
    }

    private static MailAddress MapAddress(JmapMailAddress address)
    {
        return new MailAddress
        {
            Name = address.Name ?? string.Empty,
            Address = address.Address ?? string.Empty
        };
    }

    private static Dictionary<string, DataAttribute> BuildRawData(string? rawDataJson)
    {
        var data = new Dictionary<string, DataAttribute>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(rawDataJson))
            data["rawData"] = new DataAttribute.JsonText(rawDataJson);
        return data;
    }
}
