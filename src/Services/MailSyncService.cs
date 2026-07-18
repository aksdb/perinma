using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using perinma.Messaging;
using perinma.Models;
using perinma.Storage;
using perinma.Storage.Models;

namespace perinma.Services;

public class MailSyncService
{
    private const string MailboxListSyncTokenKey = "mailboxSyncToken";
    private const string MailboxMessageSyncTokenKey = "messageSyncToken";

    private readonly SqliteStorage _storage;
    private readonly IReadOnlyDictionary<AccountType, IMailProvider> _providers;

    public MailSyncService(
        SqliteStorage storage,
        IReadOnlyDictionary<AccountType, IMailProvider> providers)
    {
        _storage = storage;
        _providers = providers;
    }

    public IReadOnlyDictionary<AccountType, IMailProvider> Providers => _providers;

    public async Task<MailSyncServiceResult> SyncAllAccountsAsync(CancellationToken cancellationToken = default)
    {
        var result = new MailSyncServiceResult();
        WeakReferenceMessenger.Default.Send(new MailSyncStartedMessage());

        try
        {
            var accounts = (await _storage.GetAllAccountsAsync())
                .Where(account => account.SupportsMail && _providers.ContainsKey(account.AccountTypeEnum))
                .ToImmutableList();

            Console.WriteLine($"Found {accounts.Count} mail accounts to sync");

            for (int i = 0; i < accounts.Count; i++)
            {
                var account = accounts[i];
                try
                {
                    WeakReferenceMessenger.Default.Send(new SyncMailAccountProgressMessage
                    {
                        AccountName = account.Name,
                        AccountIndex = i,
                        TotalAccounts = accounts.Count
                    });

                    await SyncAccountAsync(account, cancellationToken);
                    result.SyncedAccounts++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error syncing mail for account {account.Name}: {ex}");
                    result.FailedAccounts++;
                    result.Errors.Add($"{account.Name}: {ex.Message}");
                }
            }

            result.Success = result.FailedAccounts == 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during mail sync: {ex.Message}");
            result.Success = false;
            result.Errors.Add(ex.Message);
        }
        finally
        {
            WeakReferenceMessenger.Default.Send(new MailSyncEndedMessage());
        }

        return result;
    }

    public async Task<MailSyncServiceResult> ForceResyncAccountAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        var result = new MailSyncServiceResult();
        WeakReferenceMessenger.Default.Send(new MailSyncStartedMessage());

        try
        {
            var account = await _storage.GetAccountByIdAsync(accountId);
            if (account == null)
            {
                result.Success = false;
                result.Errors.Add($"Account with id {accountId} not found");
                return result;
            }

            if (!account.SupportsMail)
            {
                result.Success = false;
                result.Errors.Add($"Account {account.Name} does not support mail sync");
                return result;
            }

            Console.WriteLine($"Force mail resync requested for account: {account.Name}");

            await ClearAccountMailSyncDataAsync(account);
            Console.WriteLine($"Cleared all mail sync data for account: {account.Name}");

            try
            {
                await SyncAccountAsync(account, cancellationToken);
                result.SyncedAccounts++;
                result.Success = true;
                Console.WriteLine($"Force mail resync completed for account: {account.Name}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during force mail resync for account {account.Name}: {ex.Message}");
                result.FailedAccounts++;
                result.Errors.Add($"{account.Name}: {ex.Message}");
                result.Success = false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during force mail resync: {ex.Message}");
            result.Success = false;
            result.Errors.Add(ex.Message);
        }
        finally
        {
            WeakReferenceMessenger.Default.Send(new MailSyncEndedMessage());
        }

        return result;
    }

    public IMailProvider? GetProviderForAccountType(AccountType accountType)
    {
        return _providers.GetValueOrDefault(accountType);
    }

    private async Task SyncAccountAsync(AccountDbo account, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Syncing mail for account: {account.Name} (Type: {account.Type})");

        if (!_providers.TryGetValue(account.AccountTypeEnum, out var provider))
            throw new InvalidOperationException($"No mail provider registered for account type: {account.Type}");

        try
        {
            await SyncMailboxesAsync(provider, account, cancellationToken);

            var enabledMailboxes = (await _storage.GetMailboxesByAccountAsync(account.AccountId))
                .Where(mailbox => mailbox.Enabled == 1 && !string.IsNullOrWhiteSpace(mailbox.ExternalId))
                .ToList();

            for (int i = 0; i < enabledMailboxes.Count; i++)
            {
                var mailbox = enabledMailboxes[i];
                try
                {
                    WeakReferenceMessenger.Default.Send(new SyncMailboxProgressMessage
                    {
                        MailboxName = mailbox.Name,
                        MailboxIndex = i,
                        TotalMailboxes = enabledMailboxes.Count
                    });

                    await SyncMailboxMessagesAsync(provider, account, mailbox, cancellationToken);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error syncing messages for mailbox {mailbox.Name}: {ex}");
                }
            }
        }
        catch (ReAuthenticationRequiredException ex)
        {
            Console.WriteLine($"Account {account.Name} requires re-authentication: {ex.Message}");
            WeakReferenceMessenger.Default.Send(new ReAuthenticationRequiredMessage(ex.AccountId, ex.ProviderType));
        }
    }

    private async Task SyncMailboxesAsync(
        IMailProvider provider,
        AccountDbo account,
        CancellationToken cancellationToken)
    {
        var syncToken = await _storage.GetAccountData(account, MailboxListSyncTokenKey);
        var isFullSync = string.IsNullOrWhiteSpace(syncToken);

        MailboxSyncResult result;
        try
        {
            result = await provider.GetMailboxesAsync(account.AccountId, syncToken, cancellationToken);
            Console.WriteLine(
                $"Found {result.Mailboxes.Count} mailbox {(isFullSync ? "items" : "changes")} for account {account.Name}");
        }
        catch (Exception ex) when (!string.IsNullOrWhiteSpace(syncToken) && IsInvalidSyncTokenException(ex))
        {
            Console.WriteLine($"Mailbox sync token invalid, performing full sync: {ex.Message}");
            isFullSync = true;
            result = await provider.GetMailboxesAsync(account.AccountId, null, cancellationToken);
            Console.WriteLine($"Found {result.Mailboxes.Count} mailboxes in full sync for account {account.Name}");
        }

        var currentSyncTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        foreach (var mailbox in result.Mailboxes)
        {
            if (mailbox.Deleted)
            {
                var existingMailbox = await _storage.GetMailboxByExternalIdAsync(account.AccountId, mailbox.ExternalId);
                if (existingMailbox != null)
                {
                    await _storage.DeleteMailboxAsync(existingMailbox.MailboxId);
                    Console.WriteLine($"Deleted mailbox {mailbox.Name} from local database");
                }

                continue;
            }

            var existing = await _storage.GetMailboxByExternalIdAsync(account.AccountId, mailbox.ExternalId);
            var mailboxDbo = new MailboxDbo
            {
                AccountId = account.AccountId,
                MailboxId = existing?.MailboxId ?? string.Empty,
                ExternalId = mailbox.ExternalId,
                ParentExternalId = mailbox.ParentExternalId,
                Name = mailbox.Name,
                Role = mailbox.Role,
                UnreadCount = mailbox.UnreadCount,
                TotalCount = mailbox.TotalCount,
                Enabled = existing?.Enabled ?? (mailbox.Enabled ? 1 : 0),
                LastSync = currentSyncTime
            };

            await _storage.CreateOrUpdateMailboxAsync(mailboxDbo);
            await StoreMailboxDataAsync(mailboxDbo.MailboxId, mailbox.Data);
        }

        if (isFullSync && result.Mailboxes.Count > 0)
        {
            var deletedCount = await _storage.DeleteMailboxesNotSyncedAsync(account.AccountId, currentSyncTime);
            if (deletedCount > 0)
                Console.WriteLine($"Deleted {deletedCount} mailbox(es) that were removed remotely");
        }
        else if (isFullSync && result.Mailboxes.Count == 0)
        {
            Console.WriteLine("Skipping mailbox cleanup - no mailboxes returned from provider");
        }

        if (!string.IsNullOrWhiteSpace(result.SyncToken))
        {
            await _storage.SetAccountData(account, MailboxListSyncTokenKey, result.SyncToken);
            Console.WriteLine("Stored new mailbox sync token for next sync");
        }

        Console.WriteLine($"Synced {result.Mailboxes.Count} mailboxes for account {account.Name}");
    }

    private async Task SyncMailboxMessagesAsync(
        IMailProvider provider,
        AccountDbo account,
        MailboxDbo mailbox,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(mailbox.ExternalId))
            return;

        Console.WriteLine($"Syncing messages for mailbox: {mailbox.Name}");

        var storedSyncToken = await _storage.GetMailboxDataAsync(mailbox.MailboxId, MailboxMessageSyncTokenKey);
        var requestToken = string.IsNullOrWhiteSpace(storedSyncToken) ? null : storedSyncToken;
        var retryWithFullSync = requestToken != null;
        var authoritative = false;
        var processedMessageCount = 0;
        var mailboxIdsByExternalId = await GetMailboxIdsByExternalIdAsync(account.AccountId);
        var seenMessageExternalIds = new HashSet<string>(StringComparer.Ordinal);
        var touchedThreadIds = new HashSet<string>(StringComparer.Ordinal);
        string? finalSyncToken = requestToken;

        while (true)
        {
            MailMessageSyncResult result;
            try
            {
                result = await provider.GetMessagesAsync(
                    account.AccountId,
                    mailbox.ExternalId,
                    requestToken,
                    cancellationToken);
            }
            catch (Exception ex) when (retryWithFullSync && requestToken != null && IsInvalidSyncTokenException(ex))
            {
                Console.WriteLine($"Message sync token invalid for mailbox {mailbox.Name}, performing full sync: {ex.Message}");
                requestToken = null;
                finalSyncToken = null;
                retryWithFullSync = false;
                authoritative = false;
                processedMessageCount = 0;
                seenMessageExternalIds.Clear();
                touchedThreadIds.Clear();
                continue;
            }

            finalSyncToken = result.SyncToken;
            authoritative |= result.MissingMessagesAreAuthoritative;

            for (int i = 0; i < result.Messages.Count; i++)
            {
                var message = result.Messages[i];
                WeakReferenceMessenger.Default.Send(new SyncMailMessageProcessingProgressMessage
                {
                    MailboxName = mailbox.Name,
                    MessageIndex = processedMessageCount + i,
                    TotalMessages = processedMessageCount + result.Messages.Count
                });

                var touchedThreadId = await UpsertMessageAsync(
                    provider,
                    account,
                    mailbox,
                    message,
                    mailboxIdsByExternalId,
                    cancellationToken);

                if (!string.IsNullOrWhiteSpace(message.ExternalId))
                    seenMessageExternalIds.Add(message.ExternalId);
                if (!string.IsNullOrWhiteSpace(touchedThreadId))
                    touchedThreadIds.Add(touchedThreadId);
            }

            processedMessageCount += result.Messages.Count;

            if (string.IsNullOrWhiteSpace(result.SyncToken) || string.Equals(result.SyncToken, requestToken, StringComparison.Ordinal))
                break;

            requestToken = result.SyncToken;
            if (!result.MissingMessagesAreAuthoritative)
                retryWithFullSync = false;
        }

        if (authoritative)
        {
            await RemoveMissingMailboxMessagesAsync(mailbox.MailboxId, seenMessageExternalIds, touchedThreadIds);
        }

        foreach (var threadId in touchedThreadIds)
            await RebuildThreadAsync(threadId);

        if (authoritative && !string.IsNullOrWhiteSpace(finalSyncToken))
        {
            await _storage.SetMailboxDataAsync(mailbox.MailboxId, MailboxMessageSyncTokenKey, finalSyncToken);
            Console.WriteLine($"Stored new message sync token for mailbox {mailbox.Name}");
        }

        Console.WriteLine($"Synced {processedMessageCount} messages for mailbox {mailbox.Name}");
        WeakReferenceMessenger.Default.Send(new SyncMailMessagesProgressMessage
        {
            MailboxName = mailbox.Name,
            MessageCount = processedMessageCount
        });
    }

    private async Task<string> UpsertMessageAsync(
        IMailProvider provider,
        AccountDbo account,
        MailboxDbo mailbox,
        ProviderMailMessage summaryMessage,
        IReadOnlyDictionary<string, string> mailboxIdsByExternalId,
        CancellationToken cancellationToken)
    {
        var existingMessage = await _storage.GetMailMessageByExternalIdAsync(account.AccountId, summaryMessage.ExternalId);
        var effectiveMessage = await HydrateMessageIfNeededAsync(
            provider,
            account,
            summaryMessage,
            existingMessage,
            cancellationToken);

        var threadId = await EnsureThreadAsync(account, effectiveMessage);
        var bodyFetchedAt = HasBodyPayload(effectiveMessage)
            ? DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            : existingMessage?.BodyFetchedAt;
        var changedAt = effectiveMessage.ReceivedAtUnixTime
            ?? effectiveMessage.SentAtUnixTime
            ?? bodyFetchedAt
            ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var mailboxIds = ResolveMailboxIds(mailbox.MailboxId, effectiveMessage.MailboxExternalIds, mailboxIdsByExternalId);
        var messageDbo = new MailMessageDbo
        {
            AccountId = account.AccountId,
            ThreadId = threadId,
            MessageId = existingMessage?.MessageId ?? string.Empty,
            ExternalId = effectiveMessage.ExternalId,
            InternetMessageId = effectiveMessage.InternetMessageId,
            Subject = effectiveMessage.Subject,
            SenderName = effectiveMessage.SenderName,
            SenderAddress = effectiveMessage.SenderAddress,
            SentAt = effectiveMessage.SentAtUnixTime,
            ReceivedAt = effectiveMessage.ReceivedAtUnixTime,
            Preview = effectiveMessage.Preview,
            PlainTextBody = HasBodyPayload(effectiveMessage) ? effectiveMessage.PlainTextBody : null,
            HtmlBody = HasBodyPayload(effectiveMessage) ? effectiveMessage.HtmlBody : null,
            BodyFetchedAt = bodyFetchedAt,
            HasHtmlBody = effectiveMessage.HasHtmlBody ? 1 : 0,
            HasPlainTextBody = effectiveMessage.HasPlainTextBody ? 1 : 0,
            HasAttachments = effectiveMessage.HasAttachments ? 1 : 0,
            HasExternalResources = effectiveMessage.HasExternalResources ? 1 : 0,
            HasBlockedContent = effectiveMessage.HasBlockedContent ? 1 : 0,
            IsUnread = effectiveMessage.IsUnread ? 1 : 0,
            IsStarred = effectiveMessage.IsStarred ? 1 : 0,
            IsAnswered = effectiveMessage.IsAnswered ? 1 : 0,
            IsDraft = effectiveMessage.IsDraft ? 1 : 0,
            ChangedAt = changedAt
        };

        var messageId = await _storage.CreateOrUpdateMailMessageAsync(messageDbo, mailboxIds);
        await StoreMailMessageDataAsync(messageId, effectiveMessage);
        await StoreAttachmentMetadataAsync(messageId, existingMessage?.MessageId, effectiveMessage);
        return threadId;
    }

    private async Task<ProviderMailMessage> HydrateMessageIfNeededAsync(
        IMailProvider provider,
        AccountDbo account,
        ProviderMailMessage summaryMessage,
        MailMessageDbo? existingMessage,
        CancellationToken cancellationToken)
    {
        if (!summaryMessage.HasAttachments || summaryMessage.Attachments.Count > 0)
            return summaryMessage;

        if (existingMessage != null)
        {
            var existingAttachments = await _storage.GetAttachmentsByMessageAsync(existingMessage.MessageId);
            if (existingAttachments.Any())
                return summaryMessage;
        }

        try
        {
            var hydrated = await provider.HydrateMessageAsync(account.AccountId, summaryMessage.ExternalId, cancellationToken);
            return MergeMessages(summaryMessage, hydrated.Message);
        }
        catch (ReAuthenticationRequiredException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unable to hydrate message {summaryMessage.ExternalId}: {ex.Message}");
            return summaryMessage;
        }
    }

    private async Task<string> EnsureThreadAsync(AccountDbo account, ProviderMailMessage message)
    {
        var existingThread = await _storage.GetMailThreadByExternalIdAsync(account.AccountId, message.ThreadExternalId);
        var threadDbo = new MailThreadDbo
        {
            AccountId = account.AccountId,
            ThreadId = existingThread?.ThreadId ?? string.Empty,
            ExternalId = message.ThreadExternalId,
            Subject = message.Subject,
            ParticipantsSummary = BuildParticipantsSummary(message),
            Preview = message.Preview,
            LatestMessageReceivedAt = message.ReceivedAtUnixTime ?? message.SentAtUnixTime,
            UnreadCount = message.IsUnread ? 1 : 0,
            MessageCount = 1,
            HasAttachments = message.HasAttachments ? 1 : 0
        };

        return await _storage.CreateOrUpdateMailThreadAsync(threadDbo);
    }

    private async Task RemoveMissingMailboxMessagesAsync(
        string mailboxId,
        IReadOnlySet<string> seenMessageExternalIds,
        ISet<string> touchedThreadIds)
    {
        var existingMessages = await _storage.GetMailMessagesByMailboxAsync(mailboxId);
        foreach (var existingMessage in existingMessages)
        {
            if (string.IsNullOrWhiteSpace(existingMessage.ExternalId) || seenMessageExternalIds.Contains(existingMessage.ExternalId))
                continue;

            await _storage.RemoveMailMessageFromMailboxAsync(existingMessage.MessageId, mailboxId);
            touchedThreadIds.Add(existingMessage.ThreadId);
        }
    }

    private async Task RebuildThreadAsync(string threadId)
    {
        var messages = (await _storage.GetMailMessagesByThreadAsync(threadId)).ToList();
        if (messages.Count == 0)
            return;

        var existingThread = await _storage.GetMailThreadByIdAsync(threadId);
        var latestMessage = messages
            .OrderByDescending(message => message.ReceivedAt ?? message.SentAt ?? 0)
            .ThenByDescending(message => message.MessageId, StringComparer.Ordinal)
            .First();

        var threadDbo = new MailThreadDbo
        {
            AccountId = existingThread?.AccountId ?? latestMessage.AccountId,
            ThreadId = threadId,
            ExternalId = existingThread?.ExternalId,
            Subject = latestMessage.Subject ?? messages.FirstOrDefault(message => !string.IsNullOrWhiteSpace(message.Subject))?.Subject,
            ParticipantsSummary = BuildParticipantsSummary(messages),
            Preview = latestMessage.Preview,
            LatestMessageReceivedAt = latestMessage.ReceivedAt ?? latestMessage.SentAt,
            UnreadCount = messages.Count(message => message.MessageIsUnread),
            MessageCount = messages.Count,
            HasAttachments = messages.Any(message => message.MessageHasAttachments) ? 1 : 0
        };

        await _storage.CreateOrUpdateMailThreadAsync(threadDbo);
    }

    private async Task ClearAccountMailSyncDataAsync(AccountDbo account)
    {
        var mailboxes = (await _storage.GetMailboxesByAccountAsync(account.AccountId)).ToList();
        foreach (var mailbox in mailboxes)
            await _storage.DeleteMailboxAsync(mailbox.MailboxId);

        await _storage.SetAccountData(account, MailboxListSyncTokenKey, string.Empty);
    }

    private async Task<IReadOnlyDictionary<string, string>> GetMailboxIdsByExternalIdAsync(string accountId)
    {
        var mailboxes = await _storage.GetMailboxesByAccountAsync(accountId);
        return mailboxes
            .Where(mailbox => !string.IsNullOrWhiteSpace(mailbox.ExternalId))
            .ToDictionary(mailbox => mailbox.ExternalId!, mailbox => mailbox.MailboxId, StringComparer.Ordinal);
    }

    private static IReadOnlyCollection<string> ResolveMailboxIds(
        string currentMailboxId,
        IEnumerable<string> mailboxExternalIds,
        IReadOnlyDictionary<string, string> mailboxIdsByExternalId)
    {
        var mailboxIds = new HashSet<string>(StringComparer.Ordinal)
        {
            currentMailboxId
        };

        foreach (var mailboxExternalId in mailboxExternalIds)
        {
            if (mailboxIdsByExternalId.TryGetValue(mailboxExternalId, out var mailboxId))
                mailboxIds.Add(mailboxId);
        }

        return mailboxIds;
    }

    private async Task StoreMailboxDataAsync(string mailboxId, IReadOnlyDictionary<string, DataAttribute> data)
    {
        foreach (var dataPair in data)
        {
            switch (dataPair.Value)
            {
                case DataAttribute.Text text:
                    await _storage.SetMailboxDataAsync(mailboxId, dataPair.Key, text.value);
                    break;
                case DataAttribute.JsonText jsonText:
                    await _storage.SetMailboxDataJsonAsync(mailboxId, dataPair.Key, jsonText.value);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(data), $"Unsupported mailbox data type: {dataPair.Value.GetType().Name}");
            }
        }
    }

    private async Task StoreMailMessageDataAsync(string messageId, ProviderMailMessage message)
    {
        foreach (var dataPair in message.Data)
        {
            switch (dataPair.Value)
            {
                case DataAttribute.Text text:
                    await _storage.SetMailMessageDataAsync(messageId, dataPair.Key, text.value);
                    break;
                case DataAttribute.JsonText jsonText:
                    await _storage.SetMailMessageDataJsonAsync(messageId, dataPair.Key, jsonText.value);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(message.Data), $"Unsupported mail message data type: {dataPair.Value.GetType().Name}");
            }
        }

        await _storage.SetMailMessageDataJsonAsync(messageId, "mailboxExternalIds", JsonSerializer.Serialize(message.MailboxExternalIds));
        await _storage.SetMailMessageDataJsonAsync(messageId, "to", JsonSerializer.Serialize(message.To));
        await _storage.SetMailMessageDataJsonAsync(messageId, "cc", JsonSerializer.Serialize(message.Cc));
        await _storage.SetMailMessageDataJsonAsync(messageId, "bcc", JsonSerializer.Serialize(message.Bcc));
        await _storage.SetMailMessageDataJsonAsync(messageId, "replyTo", JsonSerializer.Serialize(message.ReplyTo));
    }

    private async Task StoreAttachmentMetadataAsync(
        string messageId,
        string? existingMessageId,
        ProviderMailMessage message)
    {
        if (message.Attachments.Count == 0)
        {
            if (!message.HasAttachments)
                await _storage.ReplaceAttachmentsAsync(messageId, []);
            return;
        }

        var attachments = message.Attachments
            .Select(attachment => new MailAttachmentDbo
            {
                MessageId = messageId,
                AttachmentId = string.Empty,
                ExternalId = attachment.ExternalId,
                FileName = attachment.FileName,
                MimeType = attachment.MimeType,
                Size = attachment.Size,
                IsInline = attachment.IsInline ? 1 : 0,
                ContentId = attachment.ContentId,
                ContentPath = null,
                DownloadedAt = null
            })
            .ToList();

        if (!string.IsNullOrWhiteSpace(existingMessageId))
        {
            var existingAttachments = (await _storage.GetAttachmentsByMessageAsync(existingMessageId))
                .Where(attachment => !string.IsNullOrWhiteSpace(attachment.ExternalId))
                .ToDictionary(attachment => attachment.ExternalId!, StringComparer.Ordinal);

            foreach (var attachment in attachments)
            {
                if (!string.IsNullOrWhiteSpace(attachment.ExternalId) &&
                    existingAttachments.TryGetValue(attachment.ExternalId, out var existingAttachment))
                {
                    attachment.ContentPath = existingAttachment.ContentPath;
                    attachment.DownloadedAt = existingAttachment.DownloadedAt;
                }
            }
        }

        await _storage.ReplaceAttachmentsAsync(messageId, attachments);

        foreach (var attachmentPair in message.Attachments.Zip(attachments, (providerAttachment, attachmentDbo) => (providerAttachment, attachmentDbo)))
        {
            foreach (var dataPair in attachmentPair.providerAttachment.Data)
            {
                switch (dataPair.Value)
                {
                    case DataAttribute.Text text:
                        await _storage.SetAttachmentDataAsync(attachmentPair.attachmentDbo.AttachmentId, dataPair.Key, text.value);
                        break;
                    case DataAttribute.JsonText jsonText:
                        await _storage.SetAttachmentDataJsonAsync(attachmentPair.attachmentDbo.AttachmentId, dataPair.Key, jsonText.value);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(message.Attachments),
                            $"Unsupported attachment data type: {dataPair.Value.GetType().Name}");
                }
            }
        }
    }

    private static ProviderMailMessage MergeMessages(ProviderMailMessage summary, ProviderMailMessage hydrated)
    {
        var mergedData = new Dictionary<string, DataAttribute>(summary.Data, StringComparer.Ordinal);
        foreach (var dataPair in hydrated.Data)
            mergedData[dataPair.Key] = dataPair.Value;

        return new ProviderMailMessage
        {
            ExternalId = summary.ExternalId,
            ThreadExternalId = string.IsNullOrWhiteSpace(hydrated.ThreadExternalId) ? summary.ThreadExternalId : hydrated.ThreadExternalId,
            InternetMessageId = hydrated.InternetMessageId ?? summary.InternetMessageId,
            Subject = hydrated.Subject ?? summary.Subject,
            SenderName = hydrated.SenderName ?? summary.SenderName,
            SenderAddress = hydrated.SenderAddress ?? summary.SenderAddress,
            SentAtUnixTime = hydrated.SentAtUnixTime ?? summary.SentAtUnixTime,
            ReceivedAtUnixTime = hydrated.ReceivedAtUnixTime ?? summary.ReceivedAtUnixTime,
            Preview = hydrated.Preview ?? summary.Preview,
            PlainTextBody = !string.IsNullOrWhiteSpace(hydrated.PlainTextBody) ? hydrated.PlainTextBody : summary.PlainTextBody,
            HtmlBody = !string.IsNullOrWhiteSpace(hydrated.HtmlBody) ? hydrated.HtmlBody : summary.HtmlBody,
            HasHtmlBody = hydrated.HasHtmlBody || summary.HasHtmlBody,
            HasPlainTextBody = hydrated.HasPlainTextBody || summary.HasPlainTextBody,
            HasAttachments = hydrated.HasAttachments || summary.HasAttachments,
            HasExternalResources = hydrated.HasExternalResources || summary.HasExternalResources,
            HasBlockedContent = hydrated.HasBlockedContent || summary.HasBlockedContent,
            IsUnread = hydrated.IsUnread,
            IsStarred = hydrated.IsStarred,
            IsAnswered = hydrated.IsAnswered,
            IsDraft = hydrated.IsDraft,
            Deleted = hydrated.Deleted || summary.Deleted,
            MailboxExternalIds = hydrated.MailboxExternalIds.Count > 0 ? hydrated.MailboxExternalIds : summary.MailboxExternalIds,
            To = hydrated.To.Count > 0 ? hydrated.To : summary.To,
            Cc = hydrated.Cc.Count > 0 ? hydrated.Cc : summary.Cc,
            Bcc = hydrated.Bcc.Count > 0 ? hydrated.Bcc : summary.Bcc,
            ReplyTo = hydrated.ReplyTo.Count > 0 ? hydrated.ReplyTo : summary.ReplyTo,
            Attachments = hydrated.Attachments.Count > 0 ? hydrated.Attachments : summary.Attachments,
            Data = mergedData
        };
    }

    private static string? BuildParticipantsSummary(ProviderMailMessage message)
    {
        var senderDisplay = FormatParticipant(message.SenderName, message.SenderAddress);
        if (string.IsNullOrWhiteSpace(senderDisplay))
            return null;
        return senderDisplay;
    }

    private static string? BuildParticipantsSummary(IReadOnlyList<MailMessageQueryResult> messages)
    {
        var participants = new List<string>(3);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var message in messages)
        {
            var participant = FormatParticipant(message.SenderName, message.SenderAddress);
            if (string.IsNullOrWhiteSpace(participant) || !seen.Add(participant))
                continue;

            if (participants.Count < 3)
                participants.Add(participant);
        }

        if (participants.Count == 0)
            return null;
        if (seen.Count > participants.Count)
            return string.Join(", ", participants) + $" +{seen.Count - participants.Count}";
        return string.Join(", ", participants);
    }

    private static string? FormatParticipant(string? name, string? address)
    {
        if (!string.IsNullOrWhiteSpace(name))
            return name;
        if (!string.IsNullOrWhiteSpace(address))
            return address;
        return null;
    }

    private static bool HasBodyPayload(ProviderMailMessage message)
    {
        return message.PlainTextBody != null || message.HtmlBody != null;
    }

    private static bool IsInvalidSyncTokenException(Exception ex)
    {
        return ex.Message.Contains("410", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("invalid", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("sync token", StringComparison.OrdinalIgnoreCase);
    }
}

public class MailSyncServiceResult
{
    public bool Success { get; set; }
    public int SyncedAccounts { get; set; }
    public int FailedAccounts { get; set; }
    public List<string> Errors { get; set; } = [];
}
