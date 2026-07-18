using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using perinma.Models;

namespace perinma.Services;

public interface IMailComposeProvider
{
    Task<MailComposeCapabilities> GetComposeCapabilitiesAsync(
        string accountId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MailIdentity>> GetSenderIdentitiesAsync(
        string accountId,
        CancellationToken cancellationToken = default);

    Task<ProviderDraftReference> SaveDraftAsync(
        string accountId,
        ProviderComposedMessage message,
        ProviderDraftReference? existingDraft = null,
        CancellationToken cancellationToken = default);

    Task DeleteDraftAsync(
        string accountId,
        ProviderDraftReference draft,
        CancellationToken cancellationToken = default);

    Task<ProviderSendResult> SendAsync(
        string accountId,
        ProviderComposedMessage message,
        ProviderDraftReference? existingDraft = null,
        CancellationToken cancellationToken = default);
}

public sealed class MailComposeCapabilities
{
    public bool SupportsDrafts { get; init; }
    public bool SupportsRemoteDrafts { get; init; }
    public bool SupportsSend { get; init; }
    public bool SupportsSenderIdentities { get; init; }
    public bool SupportsInlineAttachments { get; init; }
}

public sealed class ProviderDraftReference
{
    public string? ProviderDraftId { get; init; }
    public string? MessageExternalId { get; init; }
    public string? ThreadExternalId { get; init; }
    public string? MailboxExternalId { get; init; }
    public string? IdentityId { get; init; }
    public string? StateToken { get; init; }
    public string? RawDataJson { get; init; }
}

public sealed class ProviderComposedMessage
{
    public required MailComposeKind Kind { get; init; }
    public required MailIdentity SenderIdentity { get; init; }
    public required IReadOnlyList<MailAddress> To { get; init; }
    public required IReadOnlyList<MailAddress> Cc { get; init; }
    public required IReadOnlyList<MailAddress> Bcc { get; init; }
    public required string Subject { get; init; }
    public required string PlainTextBody { get; init; }
    public required string HtmlBody { get; init; }
    public string? InReplyTo { get; init; }
    public IReadOnlyList<string> References { get; init; } = [];
    public string? ThreadExternalId { get; init; }
    public string? SourceMessageExternalId { get; init; }
    public IReadOnlyList<ProviderComposeAttachment> Attachments { get; init; } = [];
}

public sealed class ProviderComposeAttachment
{
    public required string AttachmentId { get; init; }
    public required string FileName { get; init; }
    public required string MimeType { get; init; }
    public required string ContentPath { get; init; }
    public required long Size { get; init; }
    public bool IsInline { get; init; }
    public string? ContentId { get; init; }
    public string? ProviderReferenceJson { get; init; }
}

public sealed class ProviderSendResult
{
    public string? SentMessageExternalId { get; init; }
    public string? SentThreadExternalId { get; init; }
    public string? RawDataJson { get; init; }
}
