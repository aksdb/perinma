using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using perinma.Models;

namespace perinma.Views.Calendar.EventView;

public partial class ConferenceViewModel(CalendarEventConference conference) : ViewModelBase
{
    public string Name { get; } = conference.Name;

    public ObservableCollection<ConferenceEntryPointViewModel> EntryPoints { get; } = 
        [..conference.EntryPoints.Select(ep => new ConferenceEntryPointViewModel(ep))];
}

public partial class ConferenceEntryPointViewModel(CalendarEventConference.EntryPoint entryPoint) : ViewModelBase
{
    [ObservableProperty]
    private string _label = entryPoint.Label;

    [ObservableProperty]
    private string _uri = entryPoint.Uri;

    public string? AdditionalInfo => entryPoint.AdditionalInfo;
}
