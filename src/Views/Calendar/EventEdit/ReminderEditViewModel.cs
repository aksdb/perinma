using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace perinma.Views.Calendar.EventEdit;

public class ReminderOption
{
    public int Minutes { get; }
    public string Label { get; }

    public ReminderOption(int minutes, string label)
    {
        Minutes = minutes;
        Label = label;
    }
}

public partial class ReminderEditViewModel : ViewModelBase, IEditableField
{
    public string Label => "Reminder";

    private readonly ObservableCollection<ReminderOption> _reminderOptions = new();
    public ObservableCollection<ReminderOption> ReminderOptions => _reminderOptions;

    [ObservableProperty]
    private bool _hasReminder = true;

    [ObservableProperty]
    private int _reminderMinutes = 10;

    [ObservableProperty]
    private ReminderOption? _selectedReminderOption;

    public ReminderEditViewModel()
    {
        InitializeReminderOptions();
        SelectedReminderOption = _reminderOptions.FirstOrDefault(o => o.Minutes == 10);
    }

    public ReminderEditViewModel(int? minutes) : this()
    {
        if (minutes.HasValue && minutes.Value > 0)
        {
            HasReminder = true;
            ReminderMinutes = minutes.Value;
            SelectedReminderOption = _reminderOptions.FirstOrDefault(o => o.Minutes == minutes.Value);
        }
        else
        {
            HasReminder = false;
            SelectedReminderOption = _reminderOptions.FirstOrDefault(o => o.Minutes == 10);
        }
    }

    partial void OnSelectedReminderOptionChanged(ReminderOption? value)
    {
        if (value != null)
        {
            ReminderMinutes = value.Minutes;
        }
    }

    private void InitializeReminderOptions()
    {
        _reminderOptions.Add(new ReminderOption(0, "At start time"));
        _reminderOptions.Add(new ReminderOption(5, "5 minutes before"));
        _reminderOptions.Add(new ReminderOption(10, "10 minutes before"));
        _reminderOptions.Add(new ReminderOption(15, "15 minutes before"));
        _reminderOptions.Add(new ReminderOption(30, "30 minutes before"));
        _reminderOptions.Add(new ReminderOption(60, "1 hour before"));
        _reminderOptions.Add(new ReminderOption(120, "2 hours before"));
        _reminderOptions.Add(new ReminderOption(1440, "1 day before"));
    }
}
