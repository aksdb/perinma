using NodaTime;

namespace perinma.Views.Calendar.EventView;

public class TimeRangeViewModel : ViewModelBase
{

    public TimeRangeViewModel(LocalDateTime start, LocalDateTime end, bool fullDay = false)
    {
        var startDate = start.Date;
        var endDate = end.Date;

        if (fullDay)
        {
            // Since the event starts at the beginning of the next day, the actual range
            // we want to display is shorter.
            endDate = endDate.PlusDays(-1);

            Text = startDate == endDate ? $"{startDate}" : $"{startDate} - {endDate}";
        }
        else
        {
            if (startDate == endDate)
            {
                Text = $"{startDate}, {start.TimeOfDay} - {end.TimeOfDay}";
            }
            else
            {
                Text = $"{start} - {end}";
            }
        }
    }
    
    public string Text { get; init; }
    
}