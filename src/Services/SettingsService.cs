using System;
using System.Threading.Tasks;
using perinma.Storage;

namespace perinma.Services;

public class SettingsService(SqliteStorage storage)
{
    // Setting keys
    public static class Keys
    {
        public const string WorkingDayMonday = "calendar.workingDays.monday";
        public const string WorkingDayTuesday = "calendar.workingDays.tuesday";
        public const string WorkingDayWednesday = "calendar.workingDays.wednesday";
        public const string WorkingDayThursday = "calendar.workingDays.thursday";
        public const string WorkingDayFriday = "calendar.workingDays.friday";
        public const string WorkingDaySaturday = "calendar.workingDays.saturday";
        public const string WorkingDaySunday = "calendar.workingDays.sunday";
        public const string WorkingHoursStart = "calendar.workingHours.start";
        public const string WorkingHoursEnd = "calendar.workingHours.end";
        public const string MainWindowX = "mainWindow.position.x";
        public const string MainWindowY = "mainWindow.position.y";
        public const string MainWindowWidth = "mainWindow.size.width";
        public const string MainWindowHeight = "mainWindow.size.height";
        public const string SidebarWidth = "mainWindow.sidebar.width";
        public const string LastActiveView = "mainWindow.view.lastActive";
        public const string LastCalendarViewMode = "mainWindow.view.lastCalendarViewMode";
        public const string LastCalendarDayColumns = "mainWindow.view.lastCalendarDayColumns";
    }

    // Default values
    public static class Defaults
    {
        public const bool WorkingDayMonday = true;
        public const bool WorkingDayTuesday = true;
        public const bool WorkingDayWednesday = true;
        public const bool WorkingDayThursday = true;
        public const bool WorkingDayFriday = true;
        public const bool WorkingDaySaturday = false;
        public const bool WorkingDaySunday = false;
        public static readonly TimeSpan WorkingHoursStart = new(9, 0, 0);
        public static readonly TimeSpan WorkingHoursEnd = new(17, 0, 0);
        public const int MainWindowX = int.MinValue;
        public const int MainWindowY = int.MinValue;
        public const int MainWindowWidth = 1200;
        public const int MainWindowHeight = 600;
        public const int SidebarWidth = 250;
        public const string LastActiveView = "calendar";
        public const string LastCalendarViewMode = "Week";
        public const int LastCalendarDayColumns = 7;
    }

    // Generic accessors
    public Task<string?> GetAsync(string key) => storage.GetSettingAsync(key);
    public Task<string> GetAsync(string key, string defaultValue) => storage.GetSettingAsync(key, defaultValue);
    public Task SetAsync(string key, string value) => storage.SetSettingAsync(key, value);
    public Task<bool> GetBoolAsync(string key, bool defaultValue) => storage.GetSettingBoolAsync(key, defaultValue);
    public Task SetBoolAsync(string key, bool value) => storage.SetSettingBoolAsync(key, value);
    public Task<int> GetIntAsync(string key, int defaultValue) => storage.GetSettingIntAsync(key, defaultValue);
    public Task SetIntAsync(string key, int value) => storage.SetSettingIntAsync(key, value);

    // TimeSpan helpers
    public async Task<TimeSpan> GetTimeSpanAsync(string key, TimeSpan defaultValue)
    {
        var minutes = await storage.GetSettingIntAsync(key, (int)defaultValue.TotalMinutes);
        return TimeSpan.FromMinutes(minutes);
    }

    public Task SetTimeSpanAsync(string key, TimeSpan value)
    {
        return storage.SetSettingIntAsync(key, (int)value.TotalMinutes);
    }

    // Typed calendar settings accessors
    public Task<bool> GetWorkingDayMondayAsync() => GetBoolAsync(Keys.WorkingDayMonday, Defaults.WorkingDayMonday);
    public Task SetWorkingDayMondayAsync(bool value) => SetBoolAsync(Keys.WorkingDayMonday, value);

    public Task<bool> GetWorkingDayTuesdayAsync() => GetBoolAsync(Keys.WorkingDayTuesday, Defaults.WorkingDayTuesday);
    public Task SetWorkingDayTuesdayAsync(bool value) => SetBoolAsync(Keys.WorkingDayTuesday, value);

    public Task<bool> GetWorkingDayWednesdayAsync() => GetBoolAsync(Keys.WorkingDayWednesday, Defaults.WorkingDayWednesday);
    public Task SetWorkingDayWednesdayAsync(bool value) => SetBoolAsync(Keys.WorkingDayWednesday, value);

    public Task<bool> GetWorkingDayThursdayAsync() => GetBoolAsync(Keys.WorkingDayThursday, Defaults.WorkingDayThursday);
    public Task SetWorkingDayThursdayAsync(bool value) => SetBoolAsync(Keys.WorkingDayThursday, value);

    public Task<bool> GetWorkingDayFridayAsync() => GetBoolAsync(Keys.WorkingDayFriday, Defaults.WorkingDayFriday);
    public Task SetWorkingDayFridayAsync(bool value) => SetBoolAsync(Keys.WorkingDayFriday, value);

    public Task<bool> GetWorkingDaySaturdayAsync() => GetBoolAsync(Keys.WorkingDaySaturday, Defaults.WorkingDaySaturday);
    public Task SetWorkingDaySaturdayAsync(bool value) => SetBoolAsync(Keys.WorkingDaySaturday, value);

    public Task<bool> GetWorkingDaySundayAsync() => GetBoolAsync(Keys.WorkingDaySunday, Defaults.WorkingDaySunday);
    public Task SetWorkingDaySundayAsync(bool value) => SetBoolAsync(Keys.WorkingDaySunday, value);

    public Task<TimeSpan> GetWorkingHoursStartAsync() => GetTimeSpanAsync(Keys.WorkingHoursStart, Defaults.WorkingHoursStart);
    public Task SetWorkingHoursStartAsync(TimeSpan value) => SetTimeSpanAsync(Keys.WorkingHoursStart, value);

    public Task<TimeSpan> GetWorkingHoursEndAsync() => GetTimeSpanAsync(Keys.WorkingHoursEnd, Defaults.WorkingHoursEnd);
    public Task SetWorkingHoursEndAsync(TimeSpan value) => SetTimeSpanAsync(Keys.WorkingHoursEnd, value);

    // MainWindow settings accessors
    public Task<int> GetMainWindowXAsync() => GetIntAsync(Keys.MainWindowX, Defaults.MainWindowX);
    public Task SetMainWindowXAsync(int value) => SetIntAsync(Keys.MainWindowX, value);

    public Task<int> GetMainWindowYAsync() => GetIntAsync(Keys.MainWindowY, Defaults.MainWindowY);
    public Task SetMainWindowYAsync(int value) => SetIntAsync(Keys.MainWindowY, value);

    public Task<int> GetMainWindowWidthAsync() => GetIntAsync(Keys.MainWindowWidth, Defaults.MainWindowWidth);
    public Task SetMainWindowWidthAsync(int value) => SetIntAsync(Keys.MainWindowWidth, value);

    public Task<int> GetMainWindowHeightAsync() => GetIntAsync(Keys.MainWindowHeight, Defaults.MainWindowHeight);
    public Task SetMainWindowHeightAsync(int value) => SetIntAsync(Keys.MainWindowHeight, value);

    public Task<int> GetSidebarWidthAsync() => GetIntAsync(Keys.SidebarWidth, Defaults.SidebarWidth);
    public Task SetSidebarWidthAsync(int value) => SetIntAsync(Keys.SidebarWidth, value);

    // View state accessors
    public Task<string> GetLastActiveViewAsync() => GetAsync(Keys.LastActiveView, Defaults.LastActiveView);
    public Task SetLastActiveViewAsync(string value) => SetAsync(Keys.LastActiveView, value);

    public Task<string> GetLastCalendarViewModeAsync() => GetAsync(Keys.LastCalendarViewMode, Defaults.LastCalendarViewMode);
    public Task SetLastCalendarViewModeAsync(string value) => SetAsync(Keys.LastCalendarViewMode, value);

    public Task<int> GetLastCalendarDayColumnsAsync() => GetIntAsync(Keys.LastCalendarDayColumns, Defaults.LastCalendarDayColumns);
    public Task SetLastCalendarDayColumnsAsync(int value) => SetIntAsync(Keys.LastCalendarDayColumns, value);
}
