using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace perinma.Views.Calendar.EventEdit;

public partial class TimeRangeEditViewModel : ViewModelBase, IEditableField
{
    public string Label => "Time";

    private TimeSpan _duration;

    [ObservableProperty]
    private DateTime _startTime;

    [ObservableProperty]
    private DateTime _endTime;

    public TimeSpan Duration
    {
        get => _duration;
        set
        {
            if (SetProperty(ref _duration, value))
            {
                EndTime = StartTime.Add(_duration);
                OnPropertyChanged(nameof(EndTime));
            }
        }
    }

    public TimeRangeEditViewModel()
    {
        var now = DateTime.Now;
        var rounded = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Local);
        StartTime = rounded;
        _duration = TimeSpan.FromMinutes(30);
        EndTime = StartTime.Add(_duration);
    }

    partial void OnStartTimeChanged(DateTime value)
    {
        EndTime = value.Add(_duration);
        OnPropertyChanged(nameof(EndTime));
    }

    partial void OnEndTimeChanged(DateTime value)
    {
        if (value < StartTime)
        {
            EndTime = StartTime.Add(_duration);
            OnPropertyChanged(nameof(EndTime));
        }
        else
        {
            _duration = EndTime - StartTime;
            OnPropertyChanged(nameof(Duration));
        }
    }
}
