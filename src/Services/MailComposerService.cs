using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using perinma.Models;
using ModelMailAddress = perinma.Models.MailAddress;


namespace perinma.Services;

public sealed class MailComposerService
{
    private static readonly HtmlParser HtmlParser = new();

    public MailComposeDraft CreateDraft(
        Guid accountId,
        AccountType accountType,
        MailComposeKind kind,
        IReadOnlyList<MailIdentity> identities,
        MailComposeSourceMessage? source = null)
    {
        var draft = new MailComposeDraft
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Kind = kind,
            SourceMessageId = source?.MessageId,
            SourceMessageExternalId = source?.MessageExternalId,
            SourceThreadId = source?.ThreadId,
            SourceThreadExternalId = source?.ThreadExternalId,
            SourceInternetMessageId = source?.InternetMessageId,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var selectedIdentity = identities
            .Where(identity => identity.CanSend)
            .OrderByDescending(identity => identity.IsPrimary)
            .ThenBy(identity => identity.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .FirstOrDefault();
        if (selectedIdentity != null)
        {
            draft.SelectedIdentityId = selectedIdentity.Id;
            draft.SelectedIdentityDisplayName = selectedIdentity.DisplayName;
            draft.SelectedIdentityAddress = selectedIdentity.Address;
        }

        if (source == null)
        {
            draft.HtmlBody = "<p><br></p>";
            draft.PlainTextBody = string.Empty;
            return draft;
        }

        draft.Subject = BuildSubject(kind, source.Subject);
        draft.ToText = BuildToText(kind, source, identities);
        draft.CcText = kind == MailComposeKind.ReplyAll ? BuildCcText(source, identities) : string.Empty;
        draft.BccText = string.Empty;

        var quotedBody = BuildQuotedBody(kind, source);
        draft.HtmlBody = quotedBody.Html;
        draft.PlainTextBody = quotedBody.PlainText;
        draft.Status = MailComposeDraftStatus.LocalOnly;
        return draft;
    }

    public ProviderComposedMessage BuildProviderMessage(MailComposeDraft draft)
    {
        var to = ParseAddressList(draft.ToText);
        var cc = ParseAddressList(draft.CcText);
        var bcc = ParseAddressList(draft.BccText);
        if (to.Count == 0 && cc.Count == 0 && bcc.Count == 0)
            throw new InvalidOperationException("At least one recipient is required.");
        if (string.IsNullOrWhiteSpace(draft.SelectedIdentityAddress))
            throw new InvalidOperationException("A sender identity must be selected before saving or sending.");

        var selectedIdentity = new MailIdentity
        {
            Id = string.IsNullOrWhiteSpace(draft.SelectedIdentityId) ? draft.AccountId.ToString() : draft.SelectedIdentityId,
            DisplayName = draft.SelectedIdentityDisplayName ?? string.Empty,
            Address = draft.SelectedIdentityAddress,
            IsPrimary = true,
            CanSend = true
        };

        var finalizedHtml = FinalizeHtml(draft);
        var sanitizedBody = MailComposeHtmlSanitizer.Sanitize(finalizedHtml, allowLocalFileReferences: false);
        IReadOnlyList<string> references = string.IsNullOrWhiteSpace(draft.SourceInternetMessageId)
            ? Array.Empty<string>()
            : [draft.SourceInternetMessageId];

        return new ProviderComposedMessage
        {
            Kind = draft.Kind,
            SenderIdentity = selectedIdentity,
            To = to,
            Cc = cc,
            Bcc = bcc,
            Subject = draft.Subject,
            PlainTextBody = string.IsNullOrWhiteSpace(sanitizedBody.PlainText) ? draft.PlainTextBody : sanitizedBody.PlainText,
            HtmlBody = sanitizedBody.Html,
            InReplyTo = draft.Kind is MailComposeKind.Reply or MailComposeKind.ReplyAll ? draft.SourceInternetMessageId : null,
            References = references,
            ThreadExternalId = draft.SourceThreadExternalId,
            SourceMessageExternalId = draft.SourceMessageExternalId,
            Attachments = draft.Attachments
                .Where(attachment => !string.IsNullOrWhiteSpace(attachment.ContentPath))
                .Select(MapAttachment)
                .ToList()
        };
    }

    public IReadOnlyList<ModelMailAddress> ParseAddressList(string rawText)

    {
        if (string.IsNullOrWhiteSpace(rawText))
            return [];

        var normalizedText = rawText.Replace(';', ',');
        var collection = new MailAddressCollection();
        collection.Add(normalizedText);
        return collection
            .Cast<System.Net.Mail.MailAddress>()
            .Select(address => new ModelMailAddress

            {
                Name = address.DisplayName ?? string.Empty,
                Address = address.Address
            })
            .ToList();
    }

    public string FormatAddressList(IEnumerable<ModelMailAddress> addresses)

    {
        return string.Join(", ",
            addresses
                .Where(address => !string.IsNullOrWhiteSpace(address.Address))
                .Select(FormatAddress));
    }

    private static ProviderComposeAttachment MapAttachment(MailComposeAttachment attachment)
    {
        if (string.IsNullOrWhiteSpace(attachment.ContentPath) || !File.Exists(attachment.ContentPath))
            throw new FileNotFoundException("Compose attachment file is missing.", attachment.ContentPath);

        return new ProviderComposeAttachment
        {
            AttachmentId = attachment.Id.ToString(),
            FileName = attachment.FileName,
            MimeType = attachment.MimeType,
            ContentPath = attachment.ContentPath,
            Size = attachment.Size,
            IsInline = attachment.IsInline,
            ContentId = attachment.ContentId,
            ProviderReferenceJson = attachment.ProviderReferenceJson
        };
    }

    private static string BuildSubject(MailComposeKind kind, string? subject)
    {
        var effectiveSubject = string.IsNullOrWhiteSpace(subject) ? string.Empty : subject.Trim();
        return kind switch
        {
            MailComposeKind.Reply or MailComposeKind.ReplyAll when !effectiveSubject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase)
                => $"Re: {effectiveSubject}",
            MailComposeKind.Forward when !effectiveSubject.StartsWith("Fwd:", StringComparison.OrdinalIgnoreCase)
                => $"Fwd: {effectiveSubject}",
            _ => effectiveSubject
        };
    }

    private string BuildToText(MailComposeKind kind, MailComposeSourceMessage source, IReadOnlyList<MailIdentity> identities)
    {
        return kind switch
        {
            MailComposeKind.Reply or MailComposeKind.ReplyAll
                => FormatAddressList(GetReplyTargets(source, identities)),
            _ => string.Empty
        };
    }

    private string BuildCcText(MailComposeSourceMessage source, IReadOnlyList<MailIdentity> identities)
    {
        var identityAddresses = identities
            .Select(identity => identity.Address)
            .Where(static address => !string.IsNullOrWhiteSpace(address))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return FormatAddressList(
            source.Cc
                .Concat(source.To)
                .Where(address => !string.IsNullOrWhiteSpace(address.Address) && !identityAddresses.Contains(address.Address))
                .DistinctBy(address => address.Address, StringComparer.OrdinalIgnoreCase));
    }

    private IReadOnlyList<ModelMailAddress> GetReplyTargets(MailComposeSourceMessage source, IReadOnlyList<MailIdentity> identities)

    {
        var preferredTargets = source.ReplyTo.Count > 0 ? source.ReplyTo : source.Sender != null ? [source.Sender] : [];
        var identityAddresses = identities
            .Select(identity => identity.Address)
            .Where(static address => !string.IsNullOrWhiteSpace(address))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return preferredTargets
            .Where(address => !string.IsNullOrWhiteSpace(address.Address) && !identityAddresses.Contains(address.Address))
            .DistinctBy(address => address.Address, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private (string Html, string PlainText) BuildQuotedBody(MailComposeKind kind, MailComposeSourceMessage source)
    {
        var sourceHtml = !string.IsNullOrWhiteSpace(source.HtmlBody)
            ? MailComposeHtmlSanitizer.Sanitize(source.HtmlBody, allowLocalFileReferences: false).Html
            : ConvertPlainTextToHtml(source.PlainTextBody);
        var sourcePlainText = !string.IsNullOrWhiteSpace(source.PlainTextBody)
            ? source.PlainTextBody
            : MailComposeHtmlSanitizer.Sanitize(sourceHtml, allowLocalFileReferences: false).PlainText;

        var header = BuildReplyHeader(source);
        var html = kind == MailComposeKind.Forward
            ? $"<p><br></p><div>{header.Html}</div><blockquote>{sourceHtml}</blockquote>"
            : $"<p><br></p><blockquote>{sourceHtml}</blockquote>";
        var plainText = kind == MailComposeKind.Forward
            ? $"\n\n{header.PlainText}\n{QuotePlainText(sourcePlainText)}"
            : $"\n\n{header.PlainText}\n{QuotePlainText(sourcePlainText)}";
        return (html, plainText.TrimStart('\n'));
    }

    private (string Html, string PlainText) BuildReplyHeader(MailComposeSourceMessage source)
    {
        var sender = source.Sender != null ? FormatAddress(source.Sender) : "Unknown sender";
        var sentAt = source.SentAt?.ToLocalTime().ToString("f", CultureInfo.CurrentCulture) ?? "Unknown date";
        var to = source.To.Count > 0 ? FormatAddressList(source.To) : string.Empty;
        var cc = source.Cc.Count > 0 ? FormatAddressList(source.Cc) : string.Empty;
        var subject = string.IsNullOrWhiteSpace(source.Subject) ? "(no subject)" : WebUtility.HtmlEncode(source.Subject);

        var htmlBuilder = new StringBuilder();
        htmlBuilder.Append("<p><strong>From:</strong> ").Append(WebUtility.HtmlEncode(sender)).Append("<br>");
        htmlBuilder.Append("<strong>Sent:</strong> ").Append(WebUtility.HtmlEncode(sentAt)).Append("<br>");
        if (to.Length > 0)
            htmlBuilder.Append("<strong>To:</strong> ").Append(WebUtility.HtmlEncode(to)).Append("<br>");
        if (cc.Length > 0)
            htmlBuilder.Append("<strong>Cc:</strong> ").Append(WebUtility.HtmlEncode(cc)).Append("<br>");
        htmlBuilder.Append("<strong>Subject:</strong> ").Append(subject).Append("</p>");

        var plainTextBuilder = new StringBuilder();
        plainTextBuilder.AppendLine($"On {sentAt}, {sender} wrote:");
        return (htmlBuilder.ToString(), plainTextBuilder.ToString().TrimEnd());
    }

    private string FinalizeHtml(MailComposeDraft draft)
    {
        var sanitized = MailComposeHtmlSanitizer.Sanitize(draft.HtmlBody, allowLocalFileReferences: true);
        if (draft.Attachments.Count == 0 || string.IsNullOrWhiteSpace(sanitized.Html))
            return sanitized.Html;

        var document = HtmlParser.ParseDocument($"<body>{sanitized.Html}</body>");
        var inlineAttachmentsByPath = draft.Attachments
            .Where(attachment => attachment.IsInline && !string.IsNullOrWhiteSpace(attachment.ContentPath) && !string.IsNullOrWhiteSpace(attachment.ContentId))
            .ToDictionary(
                attachment => NormalizeContentPath(attachment.ContentPath!),
                attachment => attachment,
                StringComparer.OrdinalIgnoreCase);

        foreach (var image in document.QuerySelectorAll("img"))
        {
            var src = image.GetAttribute("src");
            if (string.IsNullOrWhiteSpace(src))
                continue;

            var localPath = TryResolveLocalPath(src);
            if (localPath == null)
                continue;
            if (!inlineAttachmentsByPath.TryGetValue(NormalizeContentPath(localPath), out var attachment))
                continue;

            image.SetAttribute("src", $"cid:{attachment.ContentId}");
        }

        return document.Body?.InnerHtml ?? sanitized.Html;
    }

    private static string FormatAddress(ModelMailAddress address)
        => string.IsNullOrWhiteSpace(address.Name)
            ? address.Address
            : $"{address.Name} <{address.Address}>";

    private static string QuotePlainText(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');
        return string.Join('\n', lines.Select(line => line.Length == 0 ? ">" : $"> {line}"));
    }

    private static string ConvertPlainTextToHtml(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var encoded = WebUtility.HtmlEncode(text);
        return $"<p>{encoded.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "<br>", StringComparison.Ordinal)}</p>";
    }

    private static string? TryResolveLocalPath(string src)
    {
        if (!Uri.TryCreate(src, UriKind.Absolute, out var uri))
            return null;
        if (!string.Equals(uri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase))
            return null;
        return uri.LocalPath;
    }

    private static string NormalizeContentPath(string path) => Path.GetFullPath(path);
}
