using CommunityToolkit.Mvvm.ComponentModel;

namespace perinma.Views.Calendar.EventEdit;

public partial class TitleEditViewModel : ViewModelBase, IEditableField
{
    public string Label => "Title";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    [NotifyPropertyChangedFor(nameof(HasValue))]
    private string _title = string.Empty;

    public string Summary => Title;
    public bool HasValue => !string.IsNullOrWhiteSpace(Title);
}
