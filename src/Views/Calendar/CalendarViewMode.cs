namespace perinma.Views.Calendar;

/// <summary>
/// Defines the available calendar view modes.
/// </summary>
public enum CalendarViewMode
{
    /// <summary>
    /// Month view showing a full calendar month grid.
    /// </summary>
    Month,

    /// <summary>
    /// Week view showing 7 days (Monday to Sunday).
    /// </summary>
    Week,

    /// <summary>
    /// Five-day view showing Monday to Friday (work week).
    /// </summary>
    FiveDays,

    /// <summary>
    /// Single day view showing one day in detail.
    /// </summary>
    Day,

    /// <summary>
    /// List/agenda view showing events in a chronological list.
    /// </summary>
    List
}
