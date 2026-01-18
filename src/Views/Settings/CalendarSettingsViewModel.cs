using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using perinma.Services;

namespace perinma.Views.Settings;

public partial class CalendarSettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;
    private bool _isLoading;

    // Working days
    [ObservableProperty]
    private bool _monday = SettingsService.Defaults.WorkingDayMonday;

    [ObservableProperty]
    private bool _tuesday = SettingsService.Defaults.WorkingDayTuesday;

    [ObservableProperty]
    private bool _wednesday = SettingsService.Defaults.WorkingDayWednesday;

    [ObservableProperty]
    private bool _thursday = SettingsService.Defaults.WorkingDayThursday;

    [ObservableProperty]
    private bool _friday = SettingsService.Defaults.WorkingDayFriday;

    [ObservableProperty]
    private bool _saturday = SettingsService.Defaults.WorkingDaySaturday;

    [ObservableProperty]
    private bool _sunday = SettingsService.Defaults.WorkingDaySunday;

    // Working hours
    [ObservableProperty]
    private TimeSpan _workingHoursStart = SettingsService.Defaults.WorkingHoursStart;

    [ObservableProperty]
    private TimeSpan _workingHoursEnd = SettingsService.Defaults.WorkingHoursEnd;

    public CalendarSettingsViewModel(SettingsService settingsService)
    {
        _settingsService = settingsService;
        _ = LoadSettingsAsync();
    }

    private async Task LoadSettingsAsync()
    {
        _isLoading = true;
        try
        {
            Monday = await _settingsService.GetWorkingDayMondayAsync();
            Tuesday = await _settingsService.GetWorkingDayTuesdayAsync();
            Wednesday = await _settingsService.GetWorkingDayWednesdayAsync();
            Thursday = await _settingsService.GetWorkingDayThursdayAsync();
            Friday = await _settingsService.GetWorkingDayFridayAsync();
            Saturday = await _settingsService.GetWorkingDaySaturdayAsync();
            Sunday = await _settingsService.GetWorkingDaySundayAsync();

            WorkingHoursStart = await _settingsService.GetWorkingHoursStartAsync();
            WorkingHoursEnd = await _settingsService.GetWorkingHoursEndAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load settings: {ex.Message}");
        }
        finally
        {
            _isLoading = false;
        }
    }

    // Property change handlers - save individual settings when changed
    partial void OnMondayChanged(bool value)
    {
        if (!_isLoading) _ = _settingsService.SetWorkingDayMondayAsync(value);
    }

    partial void OnTuesdayChanged(bool value)
    {
        if (!_isLoading) _ = _settingsService.SetWorkingDayTuesdayAsync(value);
    }

    partial void OnWednesdayChanged(bool value)
    {
        if (!_isLoading) _ = _settingsService.SetWorkingDayWednesdayAsync(value);
    }

    partial void OnThursdayChanged(bool value)
    {
        if (!_isLoading) _ = _settingsService.SetWorkingDayThursdayAsync(value);
    }

    partial void OnFridayChanged(bool value)
    {
        if (!_isLoading) _ = _settingsService.SetWorkingDayFridayAsync(value);
    }

    partial void OnSaturdayChanged(bool value)
    {
        if (!_isLoading) _ = _settingsService.SetWorkingDaySaturdayAsync(value);
    }

    partial void OnSundayChanged(bool value)
    {
        if (!_isLoading) _ = _settingsService.SetWorkingDaySundayAsync(value);
    }

    partial void OnWorkingHoursStartChanged(TimeSpan value)
    {
        if (!_isLoading) _ = _settingsService.SetWorkingHoursStartAsync(value);
    }

    partial void OnWorkingHoursEndChanged(TimeSpan value)
    {
        if (!_isLoading) _ = _settingsService.SetWorkingHoursEndAsync(value);
    }
}
