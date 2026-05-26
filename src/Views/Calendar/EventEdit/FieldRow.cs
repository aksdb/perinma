using CommunityToolkit.Mvvm.ComponentModel;

namespace perinma.Views.Calendar.EventEdit;

/// <summary>
/// Dialog-layer wrapper that owns expand/collapse state and the icon assignment
/// for a field. The field itself knows nothing about how it is presented.
/// </summary>
public partial class FieldRow : ObservableObject
{
    public IEditableField Field { get; }
    public string Icon { get; }

    [ObservableProperty]
    private bool _isExpanded;

    public FieldRow(IEditableField field, string icon, bool startExpanded = false)
    {
        Field = field;
        Icon = icon;
        _isExpanded = startExpanded;
    }
}
