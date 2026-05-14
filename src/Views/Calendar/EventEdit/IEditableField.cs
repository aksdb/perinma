namespace perinma.Views.Calendar.EventEdit;

public interface IEditableField
{
    string Label { get; }
    string Summary { get; }
    bool HasValue { get; }
}
