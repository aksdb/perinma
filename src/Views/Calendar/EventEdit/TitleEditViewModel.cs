using CommunityToolkit.Mvvm.ComponentModel;

namespace perinma.Views.Calendar.EventEdit;

public partial class TitleEditViewModel : ViewModelBase, IEditableField
{
    public string Label => "Title";

    [ObservableProperty]
    private string _title = string.Empty;

    public bool HasValue => !string.IsNullOrWhiteSpace(Title);
}
