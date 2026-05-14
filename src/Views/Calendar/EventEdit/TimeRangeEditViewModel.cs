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
    private bool _isFullDay;

    [ObservableProperty]
    private bool _isFullDaySupported;

    public TimeSpan Duration
    {
        get => _duration;
        set
        {
            if (SetProperty(ref _duration, value))
                EndTime = StartTime + _duration;
        }
    }

    public TimeRangeEditViewModel()
    {
        var now = DateTime.Now;
        var rounded = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Local);
        _duration = TimeSpan.FromMinutes(30);
        _startTime = rounded;
        _endTime = rounded + _duration;
    }

    partial void OnStartTimeChanged(DateTime value)
    {
        _endTime = value + _duration;
        OnPropertyChanged(nameof(EndTime));
        OnPropertyChanged(nameof(StartDate));
        OnPropertyChanged(nameof(StartTimeOfDay));
        OnPropertyChanged(nameof(StartHour));
        OnPropertyChanged(nameof(StartMinute));
        OnPropertyChanged(nameof(EndDate));
        OnPropertyChanged(nameof(EndTimeOfDay));
        OnPropertyChanged(nameof(EndHour));
        OnPropertyChanged(nameof(EndMinute));
        OnPropertyChanged(nameof(Summary));
    }

    partial void OnEndTimeChanged(DateTime value)
    {
        if (value < StartTime)
        {
            _endTime = StartTime + _duration;
            OnPropertyChanged(nameof(EndTime));
        }
        else
        {
            _duration = value - StartTime;
        }
        OnPropertyChanged(nameof(EndDate));
        OnPropertyChanged(nameof(EndTimeOfDay));
        OnPropertyChanged(nameof(EndHour));
        OnPropertyChanged(nameof(EndMinute));
        OnPropertyChanged(nameof(Summary));
    }

    partial void OnIsFullDayChanged(bool value)
    {
        OnPropertyChanged(nameof(Summary));
    }

    // --- Start computed properties (all write through to StartTime) ---

    public DateTime StartDate
    {
        get => StartTime.Date;
        set => StartTime = value.Date + StartTime.TimeOfDay;
    }

    public TimeSpan StartTimeOfDay
    {
        get => StartTime.TimeOfDay;
        set => StartTime = StartTime.Date + value;
    }

    public int StartHour
    {
        get => StartTime.Hour;
        set => StartTime = new DateTime(StartTime.Year, StartTime.Month, StartTime.Day,
                                        Math.Clamp(value, 0, 23), StartTime.Minute, 0, StartTime.Kind);
    }

    public int StartMinute
    {
        get => StartTime.Minute;
        set => StartTime = new DateTime(StartTime.Year, StartTime.Month, StartTime.Day,
                                        StartTime.Hour, Math.Clamp(value, 0, 59), 0, StartTime.Kind);
    }

    // --- End computed properties (all write through to EndTime) ---

    public DateTime EndDate
    {
        get => EndTime.Date;
        set => EndTime = value.Date + EndTime.TimeOfDay;
    }

    public TimeSpan EndTimeOfDay
    {
        get => EndTime.TimeOfDay;
        set => EndTime = EndTime.Date + value;
    }

    public int EndHour
    {
        get => EndTime.Hour;
        set => EndTime = new DateTime(EndTime.Year, EndTime.Month, EndTime.Day,
                                      Math.Clamp(value, 0, 23), EndTime.Minute, 0, EndTime.Kind);
    }

    public int EndMinute
    {
        get => EndTime.Minute;
        set => EndTime = new DateTime(EndTime.Year, EndTime.Month, EndTime.Day,
                                      EndTime.Hour, Math.Clamp(value, 0, 59), 0, EndTime.Kind);
    }

    // --- IEditableField ---

    public string Summary
    {
        get
        {
            if (IsFullDay)
            {
                return StartTime.Date == EndTime.Date
                    ? StartTime.ToString("MMM d")
                    : $"{StartTime:MMM d} – {EndTime:MMM d}";
            }
            return $"{StartTime:MMM d, HH:mm} – {EndTime:HH:mm}";
        }
    }

    public bool HasValue => true;
}
