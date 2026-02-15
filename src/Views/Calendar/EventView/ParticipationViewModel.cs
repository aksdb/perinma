using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using perinma.Models;

namespace perinma.Views.Calendar.EventView;

public partial class ParticipationViewModel(Participation participation) : ViewModelBase
{
    [ObservableProperty]
    private EventResponseStatus _currentState = participation.CurrentState;

    public bool HasActions => participation.Actions != null;

    public bool CanAccept => participation.Actions?.Accept != null;
    public bool CanDecline => participation.Actions?.Decline != null;
    public bool CanTentative => participation.Actions?.Tentative != null;

    public bool IsAccepted => CurrentState == EventResponseStatus.Accepted;
    public bool IsDeclined => CurrentState == EventResponseStatus.Declined;
    public bool IsTentative => CurrentState == EventResponseStatus.Tentative;
    public bool IsPending => CurrentState == EventResponseStatus.NeedsAction || CurrentState == EventResponseStatus.None;

    [RelayCommand]
    private async Task AcceptAsync()
    {
        if (participation.Actions?.Accept != null)
        {
            await participation.Actions.Accept();
            CurrentState = EventResponseStatus.Accepted;
        }
    }

    [RelayCommand]
    private async Task DeclineAsync()
    {
        if (participation.Actions?.Decline != null)
        {
            await participation.Actions.Decline();
            CurrentState = EventResponseStatus.Declined;
        }
    }

    [RelayCommand]
    private async Task TentativeAsync()
    {
        if (participation.Actions?.Tentative != null)
        {
            await participation.Actions.Tentative();
            CurrentState = EventResponseStatus.Tentative;
        }
    }
}
