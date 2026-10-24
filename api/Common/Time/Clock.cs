namespace Wintech.Hrms.Api.Common.Time;

public interface IClock
{
    DateTime UtcNow { get; }
    DateOnly Today { get; }
    DateOnly TodayIn(string timeZoneId);
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
    public DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public DateOnly TodayIn(string timeZoneId)
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));
        }
        catch (TimeZoneNotFoundException)
        {
            return Today;
        }
        catch (InvalidTimeZoneException)
        {
            return Today;
        }
    }
}
