using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using perinma.Models;

namespace perinma.Views.Calendar.EventView;

public partial class ParticipantsViewModel(List<string> participants) : ViewModelBase
{
    public ObservableCollection<ParticipantItemViewModel> Participants { get; } = 
        [..participants.Select(p => new ParticipantItemViewModel(p))];
}

public partial class ParticipantItemViewModel(string name) : ViewModelBase
{
    [ObservableProperty]
    private string _name = name;

    public string Initials => GetInitials(Name);

    private static string GetInitials(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "?";

        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return "?";

        if (parts.Length == 1)
            return parts[0][0].ToString().ToUpperInvariant();

        return (parts[0][0] + parts[1][0]).ToString().ToUpperInvariant();
    }
}
