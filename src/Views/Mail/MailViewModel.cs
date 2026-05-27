using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using perinma.Models;
using perinma.Services;
using perinma.Utils;
using perinma.Storage;
using perinma.Storage.Models;

namespace perinma.Views.Mail;

public partial class MailViewModel : ViewModelBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly SqliteStorage _storage;
    private readonly MailSyncService _mailSyncService;
    private readonly HashSet<string> _externalResourceEnabledMessageIds = new(StringComparer.Ordinal);
    private SanitizedMailHtml _selectedMessageHtmlPreview = SanitizedMailHtml.Empty;
    private int _threadLoadVersion;
    private int _messageLoadVersion;
    private int _detailLoadVersion;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private string _statusText = "Loading mail...";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedMailbox))]
    private MailboxItemViewModel? _selectedMailbox;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedThread))]
    private ThreadItemViewModel? _selectedThread;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedMessage))]
    private MessageItemViewModel? _selectedMessage;

    [ObservableProperty]
    private MailBodyKind _selectedBodyMode = MailBodyKind.Auto;

    [ObservableProperty]
    private string _selectedMessageToSummary = string.Empty;

    [ObservableProperty]
    private string _selectedMessageCcSummary = string.Empty;

    [ObservableProperty]
    private string _selectedMessageReplyToSummary = string.Empty;

    [ObservableProperty]
    private string _selectedMessageSecurityNotice = string.Empty;

    public ObservableCollection<MailboxItemViewModel> Mailboxes { get; } = [];
    public ObservableCollection<ThreadItemViewModel> Threads { get; } = [];
    public ObservableCollection<MessageItemViewModel> Messages { get; } = [];
    public ObservableCollection<AttachmentItemViewModel> SelectedMessageAttachments { get; } = [];

    public bool HasSelectedMailbox => SelectedMailbox != null;
    public bool HasSelectedThread => SelectedThread != null;
    public bool HasSelectedMessage => SelectedMessage != null;

    public bool ShowBodyText => SelectedMessage != null && ResolvePreviewKind(SelectedMessage) != MailBodyKind.Html;
    public bool ShowHtmlPreview => SelectedMessage != null && ResolvePreviewKind(SelectedMessage) == MailBodyKind.Html;

    public bool CanEnableSelectedMessageExternalResources => ShowHtmlPreview
        && SelectedMessage != null
        && (SelectedMessage.HasExternalResources || _selectedMessageHtmlPreview.HasBlockedRemoteContent)
        && !IsExternalResourcesEnabled(SelectedMessage);

    public bool SelectedMessageExternalResourcesBlocked => ShowHtmlPreview
        && SelectedMessage != null
        && (SelectedMessage.HasExternalResources || _selectedMessageHtmlPreview.HasBlockedRemoteContent)
        && !IsExternalResourcesEnabled(SelectedMessage);

    public string SelectedMessagePlainTextContent => SelectedMessage == null
        ? string.Empty
        : GetPlainTextFallback(SelectedMessage);

    public string SelectedMessageBodyText => SelectedMessage == null
        ? string.Empty
        : ResolvePreviewKind(SelectedMessage) == MailBodyKind.Html
            ? string.Empty
            : SelectedMessagePlainTextContent;

    public string SelectedMessageHtmlContent => ShowHtmlPreview
        ? _selectedMessageHtmlPreview.DocumentHtml
        : string.Empty;

    public string SelectedMessageHtmlFallbackText => ShowHtmlPreview
        ? SelectedMessagePlainTextContent
        : string.Empty;

    public MailViewModel(SqliteStorage storage, MailSyncService mailSyncService)
    {
        _storage = storage;
        _mailSyncService = mailSyncService;
        _ = LoadDataAsync(preserveSelection: false);
    }

    partial void OnSelectedMailboxChanged(MailboxItemViewModel? value)
    {
        _ = LoadThreadsAsync(value?.MailboxId, SelectedThread?.ThreadId);
    }

    partial void OnSelectedThreadChanged(ThreadItemViewModel? value)
    {
        _ = LoadMessagesAsync(value?.ThreadId, SelectedMessage?.MessageId);
    }

    partial void OnSelectedMessageChanged(MessageItemViewModel? value)
    {
        RefreshSelectedMessagePreview();
        RaisePreviewPropertiesChanged();
        _ = LoadSelectedMessageDetailsAsync(value);
    }

    partial void OnSelectedBodyModeChanged(MailBodyKind value)
    {
        RefreshSelectedMessagePreview();
        RaisePreviewPropertiesChanged();
    }

    public Task ReloadAsync() => LoadDataAsync(preserveSelection: true);

    [RelayCommand]
    private Task RefreshAsync() => RefreshCoreAsync();

    [RelayCommand]
    private Task MarkReadAsync() => ApplyActionAsync(MailActionType.MarkRead);

    [RelayCommand]
    private Task MarkUnreadAsync() => ApplyActionAsync(MailActionType.MarkUnread);

    [RelayCommand]
    private Task StarAsync() => ApplyActionAsync(MailActionType.Star);

    [RelayCommand]
    private Task UnstarAsync() => ApplyActionAsync(MailActionType.Unstar);

    [RelayCommand]
    private Task ArchiveAsync() => ApplyActionAsync(MailActionType.Archive);

    [RelayCommand]
    private Task DeleteAsync() => ApplyActionAsync(MailActionType.Delete);

    [RelayCommand]
    private void EnableExternalResources()
    {
        if (SelectedMessage == null)
            return;

        _externalResourceEnabledMessageIds.Add(SelectedMessage.MessageId);
        RefreshSelectedMessagePreview();
        RaisePreviewPropertiesChanged();
    }

    private async Task LoadDataAsync(bool preserveSelection)
    {
        var mailboxId = preserveSelection ? SelectedMailbox?.MailboxId : null;
        var threadId = preserveSelection ? SelectedThread?.ThreadId : null;
        var messageId = preserveSelection ? SelectedMessage?.MessageId : null;

        IsLoading = true;

        try
        {
            var mailboxes = (await _storage.GetAllMailboxesAsync())
                .Where(mailbox => mailbox.IsEnabled)
                .OrderBy(mailbox => mailbox.AccountSortOrder)
                .ThenBy(mailbox => mailbox.AccountName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(mailbox => GetMailboxSortOrder(mailbox.Role, mailbox.Name))
                .ThenBy(mailbox => mailbox.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(mailbox => new MailboxItemViewModel(mailbox))
                .ToList();

            ReplaceCollection(Mailboxes, mailboxes);

            if (mailboxes.Count == 0)
            {
                SelectedMailbox = null;
                SelectedThread = null;
                SelectedMessage = null;
                ReplaceCollection(Threads, []);
                ReplaceCollection(Messages, []);
                ClearSelectedMessageDetails();
                StatusText = "No synced mailboxes available.";
                return;
            }

            SelectedMailbox = SelectItem(mailboxes, mailboxId, mailbox => mailbox.MailboxId);
            await LoadThreadsAsync(SelectedMailbox?.MailboxId, threadId, messageId);
            StatusText = $"Loaded {mailboxes.Count} mailbox{(mailboxes.Count == 1 ? string.Empty : "es")}.";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to load mail: {ex.Message}";
            Console.WriteLine($"Error loading mail view: {ex}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task RefreshCoreAsync()
    {
        if (IsRefreshing)
            return;

        IsRefreshing = true;
        StatusText = "Refreshing mail...";

        try
        {
            var result = await _mailSyncService.SyncAllAccountsAsync();
            await LoadDataAsync(preserveSelection: true);

            StatusText = result.Success
                ? $"Mail refresh completed. Synced {result.SyncedAccounts} account(s)."
                : result.Errors.Count > 0
                    ? $"Mail refresh completed with errors: {string.Join("; ", result.Errors)}"
                    : "Mail refresh completed with errors.";
        }
        catch (Exception ex)
        {
            StatusText = $"Mail refresh failed: {ex.Message}";
            Console.WriteLine($"Mail refresh failed: {ex}");
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private async Task ApplyActionAsync(MailActionType action)
    {
        var mailbox = SelectedMailbox;
        var thread = SelectedThread;
        if (mailbox == null || thread == null)
            return;

        var targets = GetActionTargets();
        if (targets.Count == 0)
            return;

        StatusText = $"Applying {GetActionLabel(action)}...";

        try
        {
            foreach (var target in targets)
                await ApplyActionToMessageAsync(action, mailbox, target);

            foreach (var threadId in targets.Select(target => target.ThreadId).Distinct(StringComparer.Ordinal))
                await RebuildThreadAsync(threadId);

            await RebuildMailboxAsync(mailbox.MailboxId);
            await LoadDataAsync(preserveSelection: true);
            StatusText = $"Applied {GetActionLabel(action)} to {targets.Count} message{(targets.Count == 1 ? string.Empty : "s")}.";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to apply mail action {action}: {ex}");
            await LoadDataAsync(preserveSelection: true);
            StatusText = $"{GetActionLabel(action)} failed: {ex.Message}";
        }
    }

    private async Task ApplyActionToMessageAsync(MailActionType action, MailboxItemViewModel mailbox, MessageItemViewModel target)
    {
        var provider = _mailSyncService.GetProviderForAccountType(target.AccountType);
        if (provider != null && !string.IsNullOrWhiteSpace(target.ExternalId))
        {
            switch (action)
            {
                case MailActionType.MarkRead:
                    await provider.SetReadStateAsync(target.AccountId, target.ExternalId, isRead: true);
                    break;
                case MailActionType.MarkUnread:
                    await provider.SetReadStateAsync(target.AccountId, target.ExternalId, isRead: false);
                    break;
                case MailActionType.Star:
                    await provider.SetStarredStateAsync(target.AccountId, target.ExternalId, isStarred: true);
                    break;
                case MailActionType.Unstar:
                    await provider.SetStarredStateAsync(target.AccountId, target.ExternalId, isStarred: false);
                    break;
                case MailActionType.Archive:
                    await provider.ArchiveMessageAsync(target.AccountId, target.ExternalId);
                    break;
                case MailActionType.Delete:
                    await provider.DeleteMessageAsync(target.AccountId, target.ExternalId);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(action), action, null);
            }
        }

        switch (action)
        {
            case MailActionType.MarkRead:
                await _storage.UpdateMailMessageStateAsync(
                    target.MessageId,
                    isUnread: false,
                    isStarred: target.IsStarred,
                    isAnswered: target.IsAnswered,
                    isDraft: target.IsDraft);
                break;
            case MailActionType.MarkUnread:
                await _storage.UpdateMailMessageStateAsync(
                    target.MessageId,
                    isUnread: true,
                    isStarred: target.IsStarred,
                    isAnswered: target.IsAnswered,
                    isDraft: target.IsDraft);
                break;
            case MailActionType.Star:
                await _storage.UpdateMailMessageStateAsync(
                    target.MessageId,
                    isUnread: target.IsUnread,
                    isStarred: true,
                    isAnswered: target.IsAnswered,
                    isDraft: target.IsDraft);
                break;
            case MailActionType.Unstar:
                await _storage.UpdateMailMessageStateAsync(
                    target.MessageId,
                    isUnread: target.IsUnread,
                    isStarred: false,
                    isAnswered: target.IsAnswered,
                    isDraft: target.IsDraft);
                break;
            case MailActionType.Archive:
            case MailActionType.Delete:
                await _storage.RemoveMailMessageFromMailboxAsync(target.MessageId, mailbox.MailboxId);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }
    }

    private List<MessageItemViewModel> GetActionTargets()
    {
        return Messages.ToList();
    }

    private async Task LoadThreadsAsync(string? mailboxId, string? preferredThreadId, string? preferredMessageId = null)
    {
        var loadVersion = ++_threadLoadVersion;

        if (string.IsNullOrWhiteSpace(mailboxId))
        {
            ReplaceCollection(Threads, []);
            SelectedThread = null;
            ReplaceCollection(Messages, []);
            SelectedMessage = null;
            ClearSelectedMessageDetails();
            return;
        }

        try
        {
            var threads = (await _storage.GetMailThreadsByMailboxAsync(mailboxId))
                .Select(thread => new ThreadItemViewModel(thread))
                .ToList();

            if (loadVersion != _threadLoadVersion || SelectedMailbox?.MailboxId != mailboxId)
                return;

            ReplaceCollection(Threads, threads);
            SelectedThread = SelectItem(threads, preferredThreadId, thread => thread.ThreadId);

            if (threads.Count == 0)
            {
                ReplaceCollection(Messages, []);
                SelectedMessage = null;
                ClearSelectedMessageDetails();
                StatusText = $"{SelectedMailbox?.Name ?? "Mailbox"} is empty.";
                return;
            }

            await LoadMessagesAsync(SelectedThread?.ThreadId, preferredMessageId);
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to load threads: {ex.Message}";
            Console.WriteLine($"Error loading mail threads: {ex}");
        }
    }

    private async Task LoadMessagesAsync(string? threadId, string? preferredMessageId)
    {
        var loadVersion = ++_messageLoadVersion;

        if (string.IsNullOrWhiteSpace(threadId))
        {
            ReplaceCollection(Messages, []);
            SelectedMessage = null;
            ClearSelectedMessageDetails();
            return;
        }

        try
        {
            var messages = (await _storage.GetMailMessagesByThreadAsync(threadId))
                .Select(message => new MessageItemViewModel(message))
                .ToList();

            if (loadVersion != _messageLoadVersion || SelectedThread?.ThreadId != threadId)
                return;

            ReplaceCollection(Messages, messages);
            SelectedMessage = SelectItem(messages, preferredMessageId, message => message.MessageId)
                ?? messages.LastOrDefault();
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to load messages: {ex.Message}";
            Console.WriteLine($"Error loading mail messages: {ex}");
        }
    }

    private async Task LoadSelectedMessageDetailsAsync(MessageItemViewModel? message)
    {
        var loadVersion = ++_detailLoadVersion;

        if (message == null)
        {
            ClearSelectedMessageDetails();
            return;
        }

        try
        {
            var hydratedMessage = await EnsureMessageHydratedAsync(message);
            if (hydratedMessage != null)
            {
                if (SelectedMessage?.MessageId == message.MessageId)
                    ReplaceMessage(message.MessageId, hydratedMessage);


                return;
            }

            var toTask = LoadAddressesAsync(message.MessageId, "to");
            var ccTask = LoadAddressesAsync(message.MessageId, "cc");
            var replyToTask = LoadAddressesAsync(message.MessageId, "replyTo");
            var attachmentsTask = _storage.GetAttachmentsByMessageAsync(message.MessageId);

            await Task.WhenAll(toTask, ccTask, replyToTask, attachmentsTask);

            if (loadVersion != _detailLoadVersion || SelectedMessage?.MessageId != message.MessageId)
                return;

            SelectedMessageToSummary = FormatAddressList(toTask.Result);
            SelectedMessageCcSummary = FormatAddressList(ccTask.Result);
            SelectedMessageReplyToSummary = FormatAddressList(replyToTask.Result);

            UpdateSelectedMessageSecurityNotice(message);

            var attachments = attachmentsTask.Result
                .Select(attachment => new AttachmentItemViewModel(
                    attachment,
                    new AsyncRelayCommand(() => OpenAttachmentAsync(message, attachment))))
                .ToList();
            ReplaceCollection(SelectedMessageAttachments, attachments);
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to load message details: {ex.Message}";
            Console.WriteLine($"Error loading message details: {ex}");
            ClearSelectedMessageDetails();

            if (SelectedMessage?.MessageId == message.MessageId)
                UpdateSelectedMessageSecurityNotice(message);
        }
    }

    private async Task<List<MailAddress>> LoadAddressesAsync(string messageId, string key)
    {
        var json = await _storage.GetMailMessageDataAsync(messageId, key);
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<MailAddress>>(json, JsonOptions) ?? [];
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Failed to parse {key} addresses for message {messageId}: {ex.Message}");
            return [];
        }
    }

    private async Task<MessageItemViewModel?> EnsureMessageHydratedAsync(MessageItemViewModel message)
    {
        if (MessageHasLoadedBody(message) || string.IsNullOrWhiteSpace(message.ExternalId))
        {
            return null;
        }

        var provider = _mailSyncService.GetProviderForAccountType(message.AccountType);
        if (provider == null)
        {
            return null;
        }

        var existingMessage = await _storage.GetMailMessageByIdAsync(message.MessageId);
        if (existingMessage == null)
        {
            return null;
        }

        var hydrated = await provider.HydrateMessageAsync(message.AccountId, message.ExternalId);
        var mailboxIds = await _storage.GetMailboxIdsByMessageAsync(message.MessageId);
        var hydratedMessage = hydrated.Message;
        var bodyFetchedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var changedAt = hydratedMessage.ReceivedAtUnixTime
            ?? hydratedMessage.SentAtUnixTime
            ?? existingMessage.ChangedAt
            ?? bodyFetchedAt;

        var hydratedDbo = new MailMessageDbo
        {
            AccountId = existingMessage.AccountId,
            ThreadId = existingMessage.ThreadId,
            MessageId = existingMessage.MessageId,
            ExternalId = hydratedMessage.ExternalId,
            InternetMessageId = hydratedMessage.InternetMessageId,
            Subject = hydratedMessage.Subject,
            SenderName = hydratedMessage.SenderName,
            SenderAddress = hydratedMessage.SenderAddress,
            SentAt = hydratedMessage.SentAtUnixTime,
            ReceivedAt = hydratedMessage.ReceivedAtUnixTime,
            Preview = hydratedMessage.Preview,
            PlainTextBody = hydratedMessage.PlainTextBody,
            HtmlBody = hydratedMessage.HtmlBody,
            BodyFetchedAt = bodyFetchedAt,
            HasHtmlBody = hydratedMessage.HasHtmlBody ? 1 : 0,
            HasPlainTextBody = hydratedMessage.HasPlainTextBody ? 1 : 0,
            HasAttachments = hydratedMessage.HasAttachments ? 1 : 0,
            HasExternalResources = hydratedMessage.HasExternalResources ? 1 : 0,
            HasBlockedContent = hydratedMessage.HasBlockedContent ? 1 : 0,
            IsUnread = hydratedMessage.IsUnread ? 1 : 0,
            IsStarred = hydratedMessage.IsStarred ? 1 : 0,
            IsAnswered = hydratedMessage.IsAnswered ? 1 : 0,
            IsDraft = hydratedMessage.IsDraft ? 1 : 0,
            ChangedAt = changedAt
        };

        await _storage.CreateOrUpdateMailMessageAsync(hydratedDbo, mailboxIds);
        await StoreMailMessageDataAsync(message.MessageId, hydratedMessage);
        await StoreAttachmentMetadataAsync(message.MessageId, hydratedMessage);

        var updated = await _storage.GetMailMessageQueryByIdAsync(message.MessageId);
        return updated == null ? null : new MessageItemViewModel(updated);
    }

    private static bool MessageHasLoadedBody(MessageItemViewModel message)
    {
        return !string.IsNullOrWhiteSpace(message.PlainTextBody)
               || !string.IsNullOrWhiteSpace(message.HtmlBody)
               || (!message.HasPlainTextBody && !message.HasHtmlBody);
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
            }
        }

        await _storage.SetMailMessageDataJsonAsync(messageId, "mailboxExternalIds", JsonSerializer.Serialize(message.MailboxExternalIds));
        await _storage.SetMailMessageDataJsonAsync(messageId, "to", JsonSerializer.Serialize(message.To));
        await _storage.SetMailMessageDataJsonAsync(messageId, "cc", JsonSerializer.Serialize(message.Cc));
        await _storage.SetMailMessageDataJsonAsync(messageId, "bcc", JsonSerializer.Serialize(message.Bcc));
        await _storage.SetMailMessageDataJsonAsync(messageId, "replyTo", JsonSerializer.Serialize(message.ReplyTo));
    }

    private async Task StoreAttachmentMetadataAsync(string messageId, ProviderMailMessage message)
    {
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

        await _storage.ReplaceAttachmentsAsync(messageId, attachments);

        foreach (var attachment in attachments)
        {
            if (string.IsNullOrWhiteSpace(attachment.AttachmentId))
            {
                continue;
            }

            var sourceAttachment = message.Attachments.FirstOrDefault(candidate =>
                string.Equals(candidate.ExternalId, attachment.ExternalId, StringComparison.Ordinal));
            if (sourceAttachment == null)
            {
                continue;
            }

            foreach (var dataPair in sourceAttachment.Data)
            {
                switch (dataPair.Value)
                {
                    case DataAttribute.Text text:
                        await _storage.SetAttachmentDataAsync(attachment.AttachmentId, dataPair.Key, text.value);
                        break;
                    case DataAttribute.JsonText jsonText:
                        await _storage.SetAttachmentDataJsonAsync(attachment.AttachmentId, dataPair.Key, jsonText.value);
                        break;
                }
            }
        }
    }

    private async Task OpenAttachmentAsync(MessageItemViewModel message, MailAttachmentDbo attachment)
    {
        if (!string.IsNullOrWhiteSpace(attachment.ContentPath) && File.Exists(attachment.ContentPath))
        {
            PlatformUtil.OpenBrowser(attachment.ContentPath);
            return;
        }

        var provider = _mailSyncService.GetProviderForAccountType(message.AccountType);
        if (provider == null || string.IsNullOrWhiteSpace(message.ExternalId) || string.IsNullOrWhiteSpace(attachment.ExternalId))
        {
            StatusText = "Attachment download is not available for this message.";
            return;
        }

        var download = await provider.DownloadAttachmentAsync(message.AccountId, message.ExternalId, attachment.ExternalId);
        var attachmentDirectory = Path.Combine(Path.GetTempPath(), "perinma", "mail-attachments", message.MessageId);
        Directory.CreateDirectory(attachmentDirectory);
        var fileName = string.IsNullOrWhiteSpace(download.FileName) ? attachment.AttachmentId : download.FileName;
        var filePath = Path.Combine(attachmentDirectory, fileName);
        await File.WriteAllBytesAsync(filePath, download.Content);
        await _storage.UpdateAttachmentContentAsync(
            attachment.AttachmentId,
            filePath,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        PlatformUtil.OpenBrowser(filePath);
        StatusText = $"Opened attachment {fileName}.";
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

        var thread = new MailThreadDbo
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

        await _storage.CreateOrUpdateMailThreadAsync(thread);
    }

    private async Task RebuildMailboxAsync(string mailboxId)
    {
        var mailbox = await _storage.GetMailboxByIdAsync(mailboxId);
        if (mailbox == null)
            return;

        var messages = (await _storage.GetMailMessagesByMailboxAsync(mailboxId)).ToList();
        mailbox.TotalCount = messages.Count;
        mailbox.UnreadCount = messages.Count(message => message.MessageIsUnread);
        await _storage.CreateOrUpdateMailboxAsync(mailbox);
    }

    private void ReplaceMessage(string messageId, MessageItemViewModel updatedMessage)
    {
        for (var index = 0; index < Messages.Count; index++)
        {
            if (!string.Equals(Messages[index].MessageId, messageId, StringComparison.Ordinal))
                continue;

            Messages[index] = updatedMessage;
            if (SelectedMessage?.MessageId == messageId)
                SelectedMessage = updatedMessage;
            return;
        }

        if (SelectedMessage?.MessageId == messageId)
            SelectedMessage = updatedMessage;
    }

    private void ClearSelectedMessageDetails()
    {
        SelectedMessageToSummary = string.Empty;
        SelectedMessageCcSummary = string.Empty;
        SelectedMessageReplyToSummary = string.Empty;
        SelectedMessageSecurityNotice = string.Empty;
        ReplaceCollection(SelectedMessageAttachments, []);
    }

    private void RaisePreviewPropertiesChanged()
    {
        OnPropertyChanged(nameof(ShowBodyText));
        OnPropertyChanged(nameof(ShowHtmlPreview));
        OnPropertyChanged(nameof(CanEnableSelectedMessageExternalResources));
        OnPropertyChanged(nameof(SelectedMessageExternalResourcesBlocked));
        OnPropertyChanged(nameof(SelectedMessagePlainTextContent));
        OnPropertyChanged(nameof(SelectedMessageBodyText));
        OnPropertyChanged(nameof(SelectedMessageHtmlContent));
        OnPropertyChanged(nameof(SelectedMessageHtmlFallbackText));
    }

    private void RefreshSelectedMessagePreview()
    {
        if (SelectedMessage == null || ResolvePreviewKind(SelectedMessage) != MailBodyKind.Html)
        {
            _selectedMessageHtmlPreview = SanitizedMailHtml.Empty;
            UpdateSelectedMessageSecurityNotice(SelectedMessage);
            return;
        }

        _selectedMessageHtmlPreview = MailHtmlSanitizer.Sanitize(
            SelectedMessage.HtmlBody,
            allowExternalResources: IsExternalResourcesEnabled(SelectedMessage));

        UpdateSelectedMessageSecurityNotice(SelectedMessage);
    }

    private void UpdateSelectedMessageSecurityNotice(MessageItemViewModel? message)
    {
        if (message == null)
        {
            SelectedMessageSecurityNotice = string.Empty;
            return;
        }

        var notices = new List<string>(3);
        if (ResolvePreviewKind(message) == MailBodyKind.Html)
        {
            if ((message.HasExternalResources || _selectedMessageHtmlPreview.HasBlockedRemoteContent)
                && !IsExternalResourcesEnabled(message))
            {
                notices.Add("External resources are blocked for this message preview.");
            }
            else if (message.HasExternalResources && IsExternalResourcesEnabled(message))
            {
                notices.Add("External resources are enabled for this message preview only.");
            }

            if (_selectedMessageHtmlPreview.HasInlineContentReferences)
                notices.Add("Inline CID content is not rendered in this preview.");
        }
        else if (message.HasExternalResources)
        {
            notices.Add("This message includes external resources in its HTML body.");
        }

        if (message.HasBlockedContent && notices.Count == 0)
            notices.Add("This message includes content that was blocked during sync.");

        SelectedMessageSecurityNotice = string.Join(" ", notices);
    }

    private bool IsExternalResourcesEnabled(MessageItemViewModel message)
        => _externalResourceEnabledMessageIds.Contains(message.MessageId);

    private MailBodyKind ResolvePreviewKind(MessageItemViewModel message)
    {
        return SelectedBodyMode switch
        {
            MailBodyKind.PlainText => MailBodyKind.PlainText,
            MailBodyKind.Html => !string.IsNullOrWhiteSpace(message.HtmlBody)
                ? MailBodyKind.Html
                : MailBodyKind.PlainText,
            _ => !string.IsNullOrWhiteSpace(message.PlainTextBody)
                ? MailBodyKind.PlainText
                : !string.IsNullOrWhiteSpace(message.HtmlBody)
                    ? MailBodyKind.Html
                    : MailBodyKind.PlainText
        };
    }

    private static string GetPlainTextFallback(MessageItemViewModel message)
    {
        if (!string.IsNullOrWhiteSpace(message.PlainTextBody))
            return message.PlainTextBody!;

        if (!string.IsNullOrWhiteSpace(message.Preview))
            return message.Preview!;

        return "Message body has not been downloaded yet.";
    }

    private static int GetMailboxSortOrder(string? role, string name)
    {
        if (string.Equals(role, "inbox", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (string.Equals(role, "drafts", StringComparison.OrdinalIgnoreCase))
            return 1;
        if (string.Equals(role, "sent", StringComparison.OrdinalIgnoreCase))
            return 2;
        if (string.Equals(role, "archive", StringComparison.OrdinalIgnoreCase))
            return 3;
        if (string.Equals(role, "trash", StringComparison.OrdinalIgnoreCase))
            return 4;
        if (string.Equals(role, "junk", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "spam", StringComparison.OrdinalIgnoreCase))
            return 5;

        if (string.Equals(name, "Inbox", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (string.Equals(name, "Drafts", StringComparison.OrdinalIgnoreCase))
            return 1;
        if (string.Equals(name, "Sent", StringComparison.OrdinalIgnoreCase))
            return 2;
        if (string.Equals(name, "Archive", StringComparison.OrdinalIgnoreCase))
            return 3;
        if (string.Equals(name, "Trash", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "Bin", StringComparison.OrdinalIgnoreCase))
            return 4;
        if (string.Equals(name, "Spam", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "Junk", StringComparison.OrdinalIgnoreCase))
            return 5;

        return 10;
    }

    private static string GetActionLabel(MailActionType action) => action switch
    {
        MailActionType.MarkRead => "mark read",
        MailActionType.MarkUnread => "mark unread",
        MailActionType.Star => "star",
        MailActionType.Unstar => "unstar",
        MailActionType.Archive => "archive",
        MailActionType.Delete => "delete",
        _ => action.ToString()
    };

    private static string? BuildParticipantsSummary(IReadOnlyList<MailMessageQueryResult> messages)
    {
        var participants = new List<string>(3);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var message in messages)
        {
            var participant = FormatAddress(message.SenderName, message.SenderAddress);
            if (string.IsNullOrWhiteSpace(participant) || !seen.Add(participant))
                continue;

            if (participants.Count < 3)
                participants.Add(participant);
        }

        if (participants.Count == 0)
            return null;

        return seen.Count > participants.Count
            ? string.Join(", ", participants) + $" +{seen.Count - participants.Count}"
            : string.Join(", ", participants);
    }

    private static string FormatAddressList(IEnumerable<MailAddress> addresses)
    {
        return string.Join(", ", addresses.Select(address => FormatAddress(address.Name, address.Address)).Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string FormatAddress(string? name, string? address)
    {
        if (string.IsNullOrWhiteSpace(name))
            return address ?? string.Empty;
        if (string.IsNullOrWhiteSpace(address))
            return name;
        return $"{name} <{address}>";
    }

    private static TItem? SelectItem<TItem>(IReadOnlyList<TItem> items, string? preferredId, Func<TItem, string> idSelector)
    {
        if (!string.IsNullOrWhiteSpace(preferredId))
        {
            var matchingItem = items.FirstOrDefault(item => string.Equals(idSelector(item), preferredId, StringComparison.Ordinal));
            if (matchingItem != null)
                return matchingItem;
        }

        return items.FirstOrDefault();
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
            target.Add(item);
    }
}

public sealed class MailboxItemViewModel
{
    public MailboxItemViewModel(MailboxQueryResult mailbox)
    {
        MailboxId = mailbox.MailboxId;
        AccountId = mailbox.AccountId;
        AccountName = mailbox.AccountName;
        AccountType = mailbox.AccountTypeEnum;
        Name = mailbox.Name;
        Role = mailbox.Role;
        UnreadCount = mailbox.UnreadCount;
        TotalCount = mailbox.TotalCount;
        LastSync = mailbox.LastSync.HasValue ? DateTimeOffset.FromUnixTimeSeconds(mailbox.LastSync.Value) : null;
    }

    public string MailboxId { get; }
    public string AccountId { get; }
    public string AccountName { get; }
    public AccountType AccountType { get; }
    public string Name { get; }
    public string? Role { get; }
    public int UnreadCount { get; }
    public int TotalCount { get; }
    public DateTimeOffset? LastSync { get; }

    public string DisplayName => Name;
    public string AccountContext => AccountName;
    public string CountText => UnreadCount > 0 ? $"{UnreadCount}/{TotalCount}" : TotalCount.ToString();
    public string LastSyncText => LastSync?.ToLocalTime().ToString("g") ?? "Never synced";
}

public sealed class ThreadItemViewModel
{
    public ThreadItemViewModel(MailThreadQueryResult thread)
    {
        ThreadId = thread.ThreadId;
        AccountId = thread.AccountId;
        MailboxId = thread.MailboxId;
        Subject = string.IsNullOrWhiteSpace(thread.Subject) ? "(no subject)" : thread.Subject;
        ParticipantsSummary = string.IsNullOrWhiteSpace(thread.ParticipantsSummary) ? "Unknown sender" : thread.ParticipantsSummary;
        Preview = thread.Preview ?? string.Empty;
        UnreadCount = thread.UnreadCount;
        MessageCount = thread.MessageCount;
        HasAttachments = thread.ThreadHasAttachments;
        LatestMessageReceivedAt = thread.LatestMessageReceivedAt.HasValue
            ? DateTimeOffset.FromUnixTimeSeconds(thread.LatestMessageReceivedAt.Value)
            : null;
    }

    public string ThreadId { get; }
    public string AccountId { get; }
    public string MailboxId { get; }
    public string Subject { get; }
    public string ParticipantsSummary { get; }
    public string Preview { get; }
    public int UnreadCount { get; }
    public int MessageCount { get; }
    public bool HasAttachments { get; }
    public DateTimeOffset? LatestMessageReceivedAt { get; }

    public bool IsUnread => UnreadCount > 0;
    public string TimestampText => LatestMessageReceivedAt?.ToLocalTime().ToString("g") ?? string.Empty;
    public string CountText => MessageCount == 1 ? "1 message" : $"{MessageCount} messages";
    public string CompactTimestampText => TimestampText;
}

public sealed class MessageItemViewModel
{
    public MessageItemViewModel(MailMessageQueryResult message)
    {
        MessageId = message.MessageId;
        ThreadId = message.ThreadId;
        AccountId = message.AccountId;
        AccountType = message.AccountTypeEnum;
        ExternalId = message.ExternalId;
        Subject = string.IsNullOrWhiteSpace(message.Subject) ? "(no subject)" : message.Subject;
        SenderName = message.SenderName;
        SenderAddress = message.SenderAddress;
        Preview = message.Preview;
        PlainTextBody = message.PlainTextBody;
        HtmlBody = message.HtmlBody;
        HasHtmlBody = message.MessageHasHtmlBody;
        HasPlainTextBody = message.MessageHasPlainTextBody;
        HasAttachments = message.MessageHasAttachments;
        HasExternalResources = message.MessageHasExternalResources;
        HasBlockedContent = message.MessageHasBlockedContent;
        IsUnread = message.MessageIsUnread;
        IsStarred = message.MessageIsStarred;
        IsAnswered = message.MessageIsAnswered;
        IsDraft = message.MessageIsDraft;
        SentAt = message.SentAt.HasValue ? DateTimeOffset.FromUnixTimeSeconds(message.SentAt.Value) : null;
        ReceivedAt = message.ReceivedAt.HasValue ? DateTimeOffset.FromUnixTimeSeconds(message.ReceivedAt.Value) : null;
    }

    public string MessageId { get; }
    public string ThreadId { get; }
    public string AccountId { get; }
    public AccountType AccountType { get; }
    public string? ExternalId { get; }
    public string Subject { get; }
    public string? SenderName { get; }
    public string? SenderAddress { get; }
    public string? Preview { get; }
    public string? PlainTextBody { get; }
    public string? HtmlBody { get; }
    public bool HasHtmlBody { get; }
    public bool HasPlainTextBody { get; }
    public bool HasAttachments { get; }
    public bool HasExternalResources { get; }
    public bool HasBlockedContent { get; }
    public bool IsUnread { get; }
    public bool IsStarred { get; }
    public bool IsAnswered { get; }
    public bool IsDraft { get; }
    public DateTimeOffset? SentAt { get; }
    public DateTimeOffset? ReceivedAt { get; }

    public string SenderDisplay => string.IsNullOrWhiteSpace(SenderName)
        ? SenderAddress ?? "Unknown sender"
        : string.IsNullOrWhiteSpace(SenderAddress)
            ? SenderName
            : $"{SenderName} <{SenderAddress}>";

    public string TimestampText => (ReceivedAt ?? SentAt)?.ToLocalTime().ToString("f") ?? string.Empty;
    public string CompactTimestampText => (ReceivedAt ?? SentAt)?.ToLocalTime().ToString("g") ?? string.Empty;
}

public sealed class AttachmentItemViewModel
{
    public AttachmentItemViewModel(MailAttachmentDbo attachment, IAsyncRelayCommand openCommand)
    {
        AttachmentId = attachment.AttachmentId;
        FileName = string.IsNullOrWhiteSpace(attachment.FileName) ? "Attachment" : attachment.FileName;
        MimeType = string.IsNullOrWhiteSpace(attachment.MimeType) ? "application/octet-stream" : attachment.MimeType;
        Size = attachment.Size;
        IsInline = attachment.IsInline == 1;
        OpenCommand = openCommand;
    }

    public string AttachmentId { get; }
    public string FileName { get; }
    public string MimeType { get; }
    public int Size { get; }
    public bool IsInline { get; }
    public IAsyncRelayCommand OpenCommand { get; }

    public string Summary => Size > 0 ? $"{MimeType} • {Size:N0} bytes" : MimeType;
}
