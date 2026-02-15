using NodaTime;
using NodaTime.Text;

namespace perinma.Views.Calendar.EventView;

public class TimeRangeViewModel : ViewModelBase
{
    
    private static readonly LocalDatePattern DateDisplayPattern =
        LocalDatePattern.CreateWithInvariantCulture("MMMM d, yyyy");
    
    private static readonly LocalTimePattern TimeDisplayPattern =
        LocalTimePattern.CreateWithInvariantCulture("HH:mm");

    public TimeRangeViewModel(LocalDateTime start, LocalDateTime end, bool fullDay = false)
    {
        var startDate = start.Date;
        var endDate = end.Date;

        var startDateText = DateDisplayPattern.Format(startDate);

        if (fullDay)
        {
            // Since the event starts at the beginning of the next day, the actual range
            // we want to display is shorter.
            endDate = endDate.PlusDays(-1);

            if (startDate == endDate)
                Text = startDateText;
            else 
                Text = $"{startDateText} - {DateDisplayPattern.Format(endDate)}";
        }
        else
        {
            var startTimeText = TimeDisplayPattern.Format(start.TimeOfDay);
            var endTimeText = TimeDisplayPattern.Format(end.TimeOfDay);
            
            if (startDate == endDate)
            {
                Text = $"{startDateText}, {startTimeText} - {endTimeText}";
            }
            else
            {
                Text = $"{startDateText}, {startTimeText} - {DateDisplayPattern.Format(endDate)}, {endTimeText}";
            }
        }
    }
    
    public string Text { get; init; }
    
}