using CommunityToolkit.Mvvm.ComponentModel;

namespace perinma.Views.Calendar.EventEdit;

public partial class LocationEditViewModel : ViewModelBase, IEditableField
{
    public string Label => "Location";

    [ObservableProperty]
    private string? _location;

    public LocationEditViewModel()
    {
    }

    public LocationEditViewModel(string? location)
    {
        Location = location;
    }

    public bool HasValue => !string.IsNullOrWhiteSpace(Location);
}
