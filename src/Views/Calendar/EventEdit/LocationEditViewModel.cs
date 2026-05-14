using CommunityToolkit.Mvvm.ComponentModel;

namespace perinma.Views.Calendar.EventEdit;

public partial class LocationEditViewModel : ViewModelBase, IEditableField
{
    public string Label => "Location";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    [NotifyPropertyChangedFor(nameof(HasValue))]
    private string? _location;

    public string Summary => string.IsNullOrWhiteSpace(Location) ? "Add location" : Location!;
    public bool HasValue => !string.IsNullOrWhiteSpace(Location);

    public LocationEditViewModel() { }

    public LocationEditViewModel(string? location)
    {
        Location = location;
    }
}
