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

    [ObservableProperty]
    private DateTime _startDate;

    [ObservableProperty]
    private DateTime _endDate;

    [ObservableProperty]
    private TimeSpan _startTimeOfDay;

    [ObservableProperty]
    private TimeSpan _endTimeOfDay;

    [ObservableProperty]
    private bool _isFullDay;

    [ObservableProperty]
    private bool _isFullDaySupported;

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
        
        _startDate = StartTime.Date;
        _endDate = EndTime.Date;
        _startTimeOfDay = StartTime.TimeOfDay;
        _endTimeOfDay = EndTime.TimeOfDay;
    }

    partial void OnStartTimeChanged(DateTime value)
    {
        EndTime = value.Add(_duration);
        OnPropertyChanged(nameof(EndTime));
        
        _startDate = value.Date;
        _startTimeOfDay = value.TimeOfDay;
        OnPropertyChanged(nameof(StartDate));
        OnPropertyChanged(nameof(StartTimeOfDay));
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
        
        _endDate = value.Date;
        _endTimeOfDay = value.TimeOfDay;
        OnPropertyChanged(nameof(EndDate));
        OnPropertyChanged(nameof(EndTimeOfDay));
    }

    partial void OnStartDateChanged(DateTime value)
    {
        StartTime = value.Date + StartTime.TimeOfDay;
    }

    partial void OnEndDateChanged(DateTime value)
    {
        EndTime = value.Date + EndTime.TimeOfDay;
    }

    partial void OnStartTimeOfDayChanged(TimeSpan value)
    {
        StartTime = StartTime.Date + value;
    }

    partial void OnEndTimeOfDayChanged(TimeSpan value)
    {
        EndTime = EndTime.Date + value;
    }

    partial void OnIsFullDayChanged(bool value)
    {
        if (value)
        {
            _startDate = StartTime.Date;
            _endDate = EndTime.Date;
            OnPropertyChanged(nameof(StartDate));
            OnPropertyChanged(nameof(EndDate));
        }
    }
}
