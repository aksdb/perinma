using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using perinma.Models;
using perinma.Services;

namespace perinma.Views.Mail;

public partial class LocalDraftsViewModel : ViewModelBase
{
    private readonly MailComposeService _composeService;

    [ObservableProperty]
    private LocalDraftItemViewModel? _selectedDraft;

    [ObservableProperty]
    private string _statusText = "Loading drafts...";

    public LocalDraftsViewModel(MailComposeService composeService)
    {
        _composeService = composeService;
    }

    public ObservableCollection<LocalDraftItemViewModel> Drafts { get; } = [];

    public event Action<string?, bool>? CloseRequested;

    public async Task InitializeAsync()
    {
        await ReloadAsync();
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        var drafts = await _composeService.GetDraftsAsync();
        ReplaceCollection(Drafts, drafts.Select(draft => new LocalDraftItemViewModel(draft)));
        StatusText = drafts.Count == 0 ? "No local drafts available." : $"Loaded {drafts.Count} local draft(s).";
    }

    [RelayCommand]
    private void OpenSelectedDraft()
    {
        CloseRequested?.Invoke(SelectedDraft?.DraftId, false);
    }

    [RelayCommand]
    private async Task DeleteSelectedDraftAsync()
    {
        if (SelectedDraft == null)
            return;

        var draft = await _composeService.GetDraftAsync(SelectedDraft.DraftId);
        if (draft == null)
        {
            await ReloadAsync();
            return;
        }

        await _composeService.DiscardDraftAsync(draft);
        await ReloadAsync();
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(null, false);

    private static void ReplaceCollection<T>(ObservableCollection<T> target, System.Collections.Generic.IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
            target.Add(item);
    }
}

public sealed class LocalDraftItemViewModel
{
    public LocalDraftItemViewModel(MailComposeDraft draft)
    {
        DraftId = draft.Id.ToString();
        Subject = string.IsNullOrWhiteSpace(draft.Subject) ? "(no subject)" : draft.Subject;
        AccountId = draft.AccountId.ToString();
        Kind = draft.Kind;
        UpdatedText = draft.UpdatedAt.ToLocalTime().ToString("g");
        StatusText = draft.Status.ToString();
        RecipientSummary = string.IsNullOrWhiteSpace(draft.ToText) ? "No recipients" : draft.ToText;
    }

    public string DraftId { get; }
    public string AccountId { get; }
    public string Subject { get; }
    public MailComposeKind Kind { get; }
    public string RecipientSummary { get; }
    public string UpdatedText { get; }
    public string StatusText { get; }
}
