using CommunityToolkit.Mvvm.Input;

namespace perinma.Views.Calendar;

/// <summary>
/// Interface for calendar event ViewModels that support user responses (Accept/Decline/Tentative).
/// </summary>
public interface IRespondableEventViewModel
{
    /// <summary>
    /// Gets whether the current user can respond to this event.
    /// </summary>
    bool CanRespond { get; }

    /// <summary>
    /// Command to accept the event.
    /// </summary>
    IAsyncRelayCommand AcceptEventCommand { get; }

    /// <summary>
    /// Command to decline the event.
    /// </summary>
    IAsyncRelayCommand DeclineEventCommand { get; }

    /// <summary>
    /// Command to mark the event as tentative.
    /// </summary>
    IAsyncRelayCommand TentativeEventCommand { get; }
}
