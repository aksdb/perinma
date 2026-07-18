using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using perinma.Models;
using perinma.Services;

namespace perinma.Views.Mail;

public partial class ComposeMailViewModel : ViewModelBase
{
    private readonly MailComposeService _composeService;
    private readonly MailComposeDraft _draft;
    private readonly ObservableCollection<MailIdentity> _availableIdentities = [];
    private CancellationTokenSource? _autosaveCts;
    private bool _isInitializing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private string _subject = string.Empty;

    [ObservableProperty]
    private string _toText = string.Empty;

    [ObservableProperty]
    private string _ccText = string.Empty;

    [ObservableProperty]
    private string _bccText = string.Empty;

    [ObservableProperty]
    private string _htmlBody = string.Empty;

    [ObservableProperty]
    private string _plainTextBody = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    private MailIdentity? _selectedIdentity;

    [ObservableProperty]
    private bool _showCc;

    [ObservableProperty]
    private bool _showBcc;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "Draft not yet saved.";

    public ComposeMailViewModel(MailComposeService composeService, MailComposeDraft draft)
    {
        _composeService = composeService;
        _draft = draft;
        AvailableIdentities = new ReadOnlyObservableCollection<MailIdentity>(_availableIdentities);
        Subject = draft.Subject;
        ToText = draft.ToText;
        CcText = draft.CcText;
        BccText = draft.BccText;
        HtmlBody = draft.HtmlBody;
        PlainTextBody = draft.PlainTextBody;
        ShowCc = !string.IsNullOrWhiteSpace(draft.CcText);
        ShowBcc = !string.IsNullOrWhiteSpace(draft.BccText);
        foreach (var attachment in draft.Attachments.OrderBy(attachment => attachment.SortOrder))
            Attachments.Add(new ComposeAttachmentItemViewModel(attachment, RemoveAttachmentCommand));
    }


    public ObservableCollection<ComposeAttachmentItemViewModel> Attachments { get; } = [];

    public ReadOnlyObservableCollection<MailIdentity> AvailableIdentities { get; }


    public string WindowTitle => string.IsNullOrWhiteSpace(Subject) ? "Compose Mail" : Subject;

    public bool CanSend => !IsBusy && SelectedIdentity != null;
    public bool HasConflict => _draft.Status == MailComposeDraftStatus.Conflict;


    public event Action<bool>? CloseRequested;
    public event Action<bool>? DraftChanged;


    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _isInitializing = true;
        try
        {
            var capabilities = await _composeService.GetComposeCapabilitiesAsync(_draft.AccountId.ToString(), cancellationToken);
            var identities = await _composeService.GetSenderIdentitiesAsync(_draft.AccountId.ToString(), cancellationToken);
            _availableIdentities.Clear();
            foreach (var identity in identities)
                _availableIdentities.Add(identity);

            SelectedIdentity = identities.FirstOrDefault(identity => string.Equals(identity.Id, _draft.SelectedIdentityId, StringComparison.Ordinal))
                ?? identities.OrderByDescending(identity => identity.IsPrimary).FirstOrDefault();

            StatusText = capabilities.SupportsRemoteDrafts
                ? "Draft autosave is enabled."
                : "Only local draft autosave is available for this account.";
        }
        finally
        {
            _isInitializing = false;
        }
    }

    public async Task<IReadOnlyList<MailComposeAttachment>> AddAttachmentsAsync(IEnumerable<string> paths, bool isInline, CancellationToken cancellationToken = default)
    {
        var addedAttachments = new List<MailComposeAttachment>();
        foreach (var path in paths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var attachment = await _composeService.StageAttachmentAsync(_draft, path, isInline, cancellationToken);
            Attachments.Add(new ComposeAttachmentItemViewModel(attachment, RemoveAttachmentCommand));
            addedAttachments.Add(attachment);
        }

        QueueAutosave();
        return addedAttachments;
    }

    [RelayCommand]
    private async Task SaveDraftAsync()
    {
        await SaveDraftCoreAsync(remoteOnly: false);
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        IsBusy = true;
        try
        {
            ApplyFieldsToDraft();
            await _composeService.SendAsync(_draft);
            StatusText = "Message sent.";
            DraftChanged?.Invoke(true);
            CloseRequested?.Invoke(true);

        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DiscardAsync()
    {
        IsBusy = true;
        try
        {
            _autosaveCts?.Cancel();
            await _composeService.DiscardDraftAsync(_draft);
            StatusText = "Draft discarded.";
            DraftChanged?.Invoke(true);
            CloseRequested?.Invoke(true);

        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsNewDraftAsync()
    {
        IsBusy = true;
        try
        {
            ApplyFieldsToDraft();
            await _composeService.SaveAsNewRemoteDraftAsync(_draft);
            StatusText = "Saved as a new provider draft.";
            OnPropertyChanged(nameof(HasConflict));
            DraftChanged?.Invoke(true);
        }
        finally
        {
            IsBusy = false;
        }
    }


    [RelayCommand]
    private async Task RemoveAttachmentAsync(ComposeAttachmentItemViewModel? attachment)
    {
        if (attachment == null)
            return;

        await _composeService.RemoveAttachmentAsync(_draft, attachment.Attachment.Id);
        Attachments.Remove(attachment);
        QueueAutosave();
    }

    [RelayCommand]
    private void ToggleCc() => ShowCc = !ShowCc;

    [RelayCommand]
    private void ToggleBcc() => ShowBcc = !ShowBcc;

    partial void OnSubjectChanged(string value) => QueueAutosave();
    partial void OnToTextChanged(string value) => QueueAutosave();
    partial void OnCcTextChanged(string value) => QueueAutosave();
    partial void OnBccTextChanged(string value) => QueueAutosave();
    partial void OnHtmlBodyChanged(string value) => QueueAutosave();
    partial void OnPlainTextBodyChanged(string value) => QueueAutosave();
    partial void OnSelectedIdentityChanged(MailIdentity? value) => QueueAutosave();

    private void QueueAutosave()
    {
        if (_isInitializing || IsBusy)
            return;

        _autosaveCts?.Cancel();
        _autosaveCts = new CancellationTokenSource();
        _ = RunAutosaveAsync(_autosaveCts.Token);
    }

    private async Task RunAutosaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(600), cancellationToken);
            await SaveDraftCoreAsync(remoteOnly: true, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task SaveDraftCoreAsync(bool remoteOnly, CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        try
        {
            ApplyFieldsToDraft();
            await _composeService.SaveLocalDraftAsync(_draft, cancellationToken);
            StatusText = "Saved locally.";

            var capabilities = await _composeService.GetComposeCapabilitiesAsync(_draft.AccountId.ToString(), cancellationToken);
            if (!capabilities.SupportsRemoteDrafts)
            {
                if (!remoteOnly)
                    DraftChanged?.Invoke(false);
                return;
            }

            try
            {
                await Task.Delay(remoteOnly ? TimeSpan.FromMilliseconds(900) : TimeSpan.Zero, cancellationToken);
                await _composeService.SaveRemoteDraftAsync(_draft, cancellationToken);
                StatusText = "Saved to provider draft.";
                OnPropertyChanged(nameof(HasConflict));
                if (!remoteOnly)
                    DraftChanged?.Invoke(true);
            }
            catch (FormatException)
            {
                StatusText = "Saved locally. Fix recipient formatting before syncing remotely.";
            }
            catch (MailComposeConflictException ex)
            {
                StatusText = ex.Message;
                OnPropertyChanged(nameof(HasConflict));
            }
            catch (InvalidOperationException)
            {
                StatusText = "Saved locally. Remote draft sync is waiting for valid compose data.";
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyFieldsToDraft()
    {
        _draft.Subject = Subject;
        _draft.ToText = ToText;
        _draft.CcText = CcText;
        _draft.BccText = BccText;
        _draft.HtmlBody = HtmlBody;
        _draft.PlainTextBody = PlainTextBody;
        _draft.SelectedIdentityId = SelectedIdentity?.Id;
        _draft.SelectedIdentityDisplayName = SelectedIdentity?.DisplayName;
        _draft.SelectedIdentityAddress = SelectedIdentity?.Address;
    }
}

public sealed class ComposeAttachmentItemViewModel
{
    public ComposeAttachmentItemViewModel(MailComposeAttachment attachment, IAsyncRelayCommand<ComposeAttachmentItemViewModel?> removeCommand)
    {
        Attachment = attachment;
        RemoveCommand = removeCommand;
    }

    public MailComposeAttachment Attachment { get; }
    public IAsyncRelayCommand<ComposeAttachmentItemViewModel?> RemoveCommand { get; }
    public string FileName => Attachment.FileName;
    public string Summary => Attachment.Size > 0 ? $"{Attachment.MimeType} • {Attachment.Size:N0} bytes" : Attachment.MimeType;
    public bool IsInline => Attachment.IsInline;
}
