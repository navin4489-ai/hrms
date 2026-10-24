using Wintech.Hrms.Api.Common.Time;

namespace Wintech.Hrms.Api.Modules.Attendance;

/// <summary>
/// Organisation-local ("business") time helpers. All punch/check-in/out values the API returns
/// are business times in the org time zone (Asia/Kolkata, UTC+05:30), never raw UTC
/// (docs/API_CONTRACT.md: times are HH:mm, dates YYYY-MM-DD).
/// </summary>
internal static class AttendanceTimeHelper
{
    public const string TimeZoneId = "Asia/Kolkata";

    private static readonly Lazy<TimeZoneInfo> Zone = new(() =>
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId); }
        catch (TimeZoneNotFoundException) { return Fallback(); }
        catch (InvalidTimeZoneException) { return Fallback(); }

        static TimeZoneInfo Fallback() =>
            TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromMinutes(330), "India Standard Time", "IST");
    });

    public static DateTime NowLocal(DateTime utcNow) =>
        TimeZoneInfo.ConvertTimeFromUtc(utcNow, Zone.Value);

    public static DateTime TodayLocal(DateTime utcNow) => NowLocal(utcNow).Date;

    public static TimeOnly TimeNowLocal(DateTime utcNow) => TimeOnly.FromDateTime(NowLocal(utcNow));

    public static DateOnly DateLocal(DateTime utcNow) => DateOnly.FromDateTime(NowLocal(utcNow));

    public static DateOnly FirstDayOfMonth(DateOnly monthStart) => new(monthStart.Year, monthStart.Month, 1);

    public static DateOnly LastDayOfMonth(DateOnly monthStart) => new(monthStart.Year, monthStart.Month, DateTime.DaysInMonth(monthStart.Year, monthStart.Month));

    public static string WeekDayName(DateOnly date) =>
        date.DayOfWeek switch
        {
            DayOfWeek.Monday => "Mon",
            DayOfWeek.Tuesday => "Tue",
            DayOfWeek.Wednesday => "Wed",
            DayOfWeek.Thursday => "Thu",
            DayOfWeek.Friday => "Fri",
            DayOfWeek.Saturday => "Sat",
            _ => "Sun"
        };

    public static int Minutes(TimeOnly time) => time.Hour * 60 + time.Minute;

    /// <summary>Hours between two punch times minus break; handles night shifts crossing midnight.</summary>
    public static decimal? WorkedHours(TimeOnly? checkIn, TimeOnly? checkOut, int breakMinutes)
    {
        if (checkIn is null || checkOut is null) return null;
        var span = Minutes(checkOut.Value) - Minutes(checkIn.Value);
        if (span < 0) span += 24 * 60;
        span -= breakMinutes;
        if (span < 0) span = 0;
        return Math.Round(span / 60m, 2);
    }
}
