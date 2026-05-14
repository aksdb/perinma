using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using perinma.Models;
using perinma.Storage;
using perinma.Storage.Models;

namespace perinma.Views.Calendar.EventEdit;

public partial class ParticipantsEditViewModel : ObservableObject, IEditableField
{
    private readonly SqliteStorage _storage;
    private CancellationTokenSource? _searchCancellationToken;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isDropdownOpen;

    public ObservableCollection<ParticipantEditItemViewModel> SelectedParticipants { get; } = [];

    public ObservableCollection<ParticipantEditItemViewModel> SearchResults { get; } = [];

    public string Label => "Participants";

    public ParticipantsEditViewModel(SqliteStorage storage)
    {
        _storage = storage;
        SelectedParticipants.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(HasValue));
        };
    }

    public ParticipantsEditViewModel(SqliteStorage storage, List<CalendarEventParticipant> existingParticipants)
        : this(storage)
    {
        foreach (var participant in existingParticipants)
        {
            var itemViewModel = new ParticipantEditItemViewModel(participant.Email, participant.Name)
            {
                IsOptional = participant.IsOptional
            };
            itemViewModel.RemoveAction = () => RemoveParticipant(itemViewModel);
            SelectedParticipants.Add(itemViewModel);
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        _ = SearchContactsAsync();
    }

    [RelayCommand]
    private async Task SearchContactsAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            SearchResults.Clear();
            IsDropdownOpen = false;
            return;
        }

        _searchCancellationToken?.Cancel();
        _searchCancellationToken = new CancellationTokenSource();

        try
        {
            var contacts = await _storage.SearchContactsAsync(SearchText, 20, _searchCancellationToken.Token);
            SearchResults.Clear();

            foreach (var contact in contacts.Take(20))
            {
                SearchResults.Add(new ParticipantEditItemViewModel(contact));
            }

            IsDropdownOpen = SearchResults.Count > 0;
        }
        catch (TaskCanceledException)
        {
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error searching contacts: {ex.Message}");
        }
    }

    [RelayCommand]
    private void AddFromContact(ParticipantEditItemViewModel? searchResult)
    {
        if (searchResult == null || string.IsNullOrWhiteSpace(searchResult.Email))
            return;

        if (SelectedParticipants.Any(p => p.Email.Equals(searchResult.Email, StringComparison.OrdinalIgnoreCase)))
            return;

        searchResult.RemoveAction = () => RemoveParticipant(searchResult);
        SelectedParticipants.Add(searchResult);

        SearchText = string.Empty;
        SearchResults.Clear();
        IsDropdownOpen = false;
    }

    [RelayCommand]
    private void AddCustomParticipant()
    {
        var email = SearchText.Trim();

        if (!IsValidEmail(email))
            return;

        if (SelectedParticipants.Any(p => p.Email.Equals(email, StringComparison.OrdinalIgnoreCase)))
            return;

        var participant = new ParticipantEditItemViewModel(email);
        participant.RemoveAction = () => RemoveParticipant(participant);
        SelectedParticipants.Add(participant);

        SearchText = string.Empty;
        SearchResults.Clear();
        IsDropdownOpen = false;
    }

    [RelayCommand]
    private void RemoveParticipant(ParticipantEditItemViewModel participant)
    {
        SelectedParticipants.Remove(participant);
    }

    public List<CalendarEventParticipant> GetParticipants()
    {
        return SelectedParticipants
            .Select(p => new CalendarEventParticipant
            {
                Email = p.Email,
                Name = p.DisplayName != p.Email ? p.DisplayName : null,
                IsOptional = p.IsOptional
            })
            .ToList();
    }

    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }

    public string Summary
    {
        get
        {
            if (SelectedParticipants.Count == 0) return "Add participants";
            if (SelectedParticipants.Count == 1) return SelectedParticipants[0].DisplayName;
            return $"{SelectedParticipants[0].DisplayName} +{SelectedParticipants.Count - 1} more";
        }
    }

    public bool HasValue => SelectedParticipants.Count > 0;
}
