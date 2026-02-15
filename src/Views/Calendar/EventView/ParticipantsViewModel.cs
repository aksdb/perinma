using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using perinma.Models;

namespace perinma.Views.Calendar.EventView;

public partial class ParticipantsViewModel(List<CalendarEventParticipant> participants) : ViewModelBase
{
    public ObservableCollection<ParticipantViewModel> Participants { get; } =
        [..participants.Select(p => new ParticipantViewModel(p))];

    public async Task EnrichParticipantsAsync(CancellationToken cancellationToken = default)
    {
        var enrichTasks = Participants.Select(p => p.EnrichFromContactsAsync(cancellationToken));
        await Task.WhenAll(enrichTasks);
    }
}
