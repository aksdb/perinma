using CommunityToolkit.Mvvm.ComponentModel;
using perinma.Models;

namespace perinma.Views.Calendar.EventEdit;

public partial class DescriptionEditViewModel : ViewModelBase, IEditableField
{
    public string Label => "Description";

    [ObservableProperty]
    private string? _description;

    public DescriptionEditViewModel()
    {
    }

    public DescriptionEditViewModel(RichText? content)
    {
        if (content is RichText.SimpleText st)
        {
            Description = st.value;
        }
        else if (content is RichText.HTML html)
        {
            Description = html.value;
        }
    }

    public RichText? GetRichText()
    {
        if (string.IsNullOrWhiteSpace(Description))
            return null;

        return new RichText.HTML(Description);
    }
}
