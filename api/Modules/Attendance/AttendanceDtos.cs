using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Wintech.Hrms.Api.Domain.Attendance;

namespace Wintech.Hrms.Api.Modules.Attendance;

// ------------------------------------------------------------------ attendance calendars

/// <summary>One day's attendance row (docs/API_CONTRACT.md §7 month items / today).</summary>
public sealed record AttendanceDayDto(
    DateOnly WorkDate,
    AttendanceDayStatus Status,
    string? ShiftName,
    [property: JsonConverter(typeof(HhMmTimeOnlyConverter))] TimeOnly? CheckIn,
    [property: JsonConverter(typeof(HhMmTimeOnlyConverter))] TimeOnly? CheckOut,
    decimal? WorkedHours,
    int? LateByMinutes,
    bool IsHalfDay,
    string? Source,
    string? CorrectionStatus);

public sealed record AttendanceSummaryDto(
    int Present,
    int Absent,
    int HalfDay,
    int Leave,
    int Holidays,
    int WeekOffs,
    int Late,
    int PendingCorrections);

/// <summary>GET /attendance — paged day rows plus the month summary (summary is part of the response).</summary>
public sealed class AttendanceMonthResponse
{
    public IReadOnlyList<AttendanceDayDto> Items { get; init; } = [];
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int Total { get; init; }
    public bool HasNextPage { get; init; }
    public AttendanceSummaryDto Summary { get; init; } = new(0, 0, 0, 0, 0, 0, 0, 0);
}

/// <summary>GET /attendance/summary.</summary>
public sealed record AttendanceRangeSummaryDto(
    int Present,
    int Absent,
    int HalfDay,
    int Leave,
    int Holidays,
    int WeekOffs,
    int Late,
    decimal OvertimeHours,
    decimal WorkedHours);

// ------------------------------------------------------------------ punching

public sealed class PunchRequest
{
    /// <summary>Mobile | Web | Biometric (FR-EMP-031).</summary>
    [Required]
    [StringLength(30)]
    public string Source { get; set; } = "Mobile";

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? AccuracyMeters { get; set; }
}

public sealed record PunchResponse(
    DateOnly WorkDate,
    [property: JsonConverter(typeof(HhMmTimeOnlyConverter))] TimeOnly? CheckIn,
    [property: JsonConverter(typeof(HhMmTimeOnlyConverter))] TimeOnly? CheckOut,
    AttendanceDayStatus Status,
    string Message);

/// <summary>Multipart punch form allowing an optional selfie plus geolocation.</summary>
public sealed class PunchForm
{
    [StringLength(30)]
    public string Source { get; set; } = "Mobile";

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? AccuracyMeters { get; set; }

    /// <summary>Optional selfie image captured at punch time (stored privately, admin-only).</summary>
    public IFormFile? Photo { get; set; }
}

/// <summary>Capture settings an employee's app reads before punching (what to collect).</summary>
public sealed record AttendanceCaptureSettingsDto(bool CaptureLocation, bool CapturePhoto);

// ------------------------------------------------------------------ shifts, roster, swap

public sealed record ShiftDto(
    Guid Id,
    string Name,
    [property: JsonConverter(typeof(HhMmTimeOnlyConverter))] TimeOnly StartTime,
    [property: JsonConverter(typeof(HhMmTimeOnlyConverter))] TimeOnly EndTime,
    int GraceMinutes,
    int BreakMinutes,
    IReadOnlyList<string> WeekDays);

public sealed record AdminShiftDto(
    Guid Id,
    string Code,
    string Name,
    [property: JsonConverter(typeof(HhMmTimeOnlyConverter))] TimeOnly StartTime,
    [property: JsonConverter(typeof(HhMmTimeOnlyConverter))] TimeOnly EndTime,
    int GraceMinutes,
    int BreakMinutes,
    IReadOnlyList<string> WeekDays,
    bool IsDefault,
    bool IsActive);

public sealed class UpsertShiftRequest
{
    [Required, StringLength(50)] public string Code { get; init; } = string.Empty;
    [Required, StringLength(200)] public string Name { get; init; } = string.Empty;
    [Required] public TimeOnly StartTime { get; init; }
    [Required] public TimeOnly EndTime { get; init; }
    [Range(0, 180)] public int GraceMinutes { get; init; }
    [Range(0, 600)] public int BreakMinutes { get; init; }
    [MinLength(1)] public List<string> WeekDays { get; init; } = [];
    public bool IsDefault { get; init; }
    public bool IsActive { get; init; } = true;
}

public sealed record RosterEntryDto(
    DateOnly Date,
    Guid ShiftId,
    string ShiftName,
    [property: JsonConverter(typeof(HhMmTimeOnlyConverter))] TimeOnly StartTime,
    [property: JsonConverter(typeof(HhMmTimeOnlyConverter))] TimeOnly EndTime);

public sealed class ShiftSwapRequestDto
{
    [Required]
    public DateOnly RosterDate { get; set; }

    [Required]
    public Guid ProposedWithEmployeeId { get; set; }

    public Guid? ProposedShiftId { get; set; }

    [Required]
    [StringLength(1000)]
    public string Reason { get; set; } = string.Empty;
}

public sealed record ShiftSwapCreatedDto(Guid Id, Guid ApprovalTaskId);

// ------------------------------------------------------------------ corrections

public sealed class AttendanceCorrectionCreateDto
{
    [Required]
    public DateOnly WorkDate { get; set; }

    /// <summary>MissingPunch | IncorrectCheckIn | IncorrectCheckOut | IncorrectTimes | ShiftCorrection | Overtime | Other.</summary>
    [Required]
    [StringLength(40)]
    public string Type { get; set; } = string.Empty;

    [property: JsonConverter(typeof(HhMmTimeOnlyConverter))]
    public TimeOnly? ProposedCheckIn { get; set; }

    [property: JsonConverter(typeof(HhMmTimeOnlyConverter))]
    public TimeOnly? ProposedCheckOut { get; set; }

    [Required]
    [StringLength(1000)]
    public string Reason { get; set; } = string.Empty;

    public string? AttachmentUrl { get; set; }
}

public sealed record AttendanceCorrectionCreatedDto(Guid Id, AttendanceCorrectionStatus Status, Guid ApprovalTaskId);

public sealed record AttendanceCorrectionListItem(
    Guid Id,
    DateOnly WorkDate,
    AttendanceCorrectionType Type,
    [property: JsonConverter(typeof(HhMmTimeOnlyConverter))] TimeOnly? ProposedCheckIn,
    [property: JsonConverter(typeof(HhMmTimeOnlyConverter))] TimeOnly? ProposedCheckOut,
    [property: JsonConverter(typeof(HhMmTimeOnlyConverter))] TimeOnly? OriginalCheckIn,
    [property: JsonConverter(typeof(HhMmTimeOnlyConverter))] TimeOnly? OriginalCheckOut,
    string Reason,
    AttendanceCorrectionStatus Status,
    DateTime SubmittedAt,
    string? ApproverName,
    Guid? ApprovalTaskId);

public sealed record DecisionHistoryEntry(string? Step, string Action, string ActorName, string? Comment, DateTime At);

public sealed record AttendanceCorrectionDetail(
    Guid Id,
    DateOnly WorkDate,
    AttendanceCorrectionType Type,
    [property: JsonConverter(typeof(HhMmTimeOnlyConverter))] TimeOnly? ProposedCheckIn,
    [property: JsonConverter(typeof(HhMmTimeOnlyConverter))] TimeOnly? ProposedCheckOut,
    [property: JsonConverter(typeof(HhMmTimeOnlyConverter))] TimeOnly? OriginalCheckIn,
    [property: JsonConverter(typeof(HhMmTimeOnlyConverter))] TimeOnly? OriginalCheckOut,
    string Reason,
    string? AttachmentUrl,
    AttendanceCorrectionStatus Status,
    DateTime SubmittedAt,
    DateTime? DecidedAt,
    string? ApproverName,
    Guid? ApprovalTaskId,
    IReadOnlyList<DecisionHistoryEntry> DecisionHistory);

// ------------------------------------------------------------------ holidays

public sealed record HolidayDto(Guid Id, DateOnly Date, string Name, HolidayKind Type, Guid? LocationId);

public sealed class UpsertHolidayRequest
{
    [Required]
    public DateOnly Date { get; init; }

    [Required, StringLength(200)]
    public string Name { get; init; } = string.Empty;

    [Required]
    public HolidayKind Type { get; init; }

    public Guid? LocationId { get; init; }
}

// ------------------------------------------------------------------ dashboard slice

public sealed record DashboardShiftDto(
    string Name,
    [property: JsonConverter(typeof(HhMmTimeOnlyConverter))] TimeOnly StartTime,
    [property: JsonConverter(typeof(HhMmTimeOnlyConverter))] TimeOnly EndTime,
    int GraceMinutes);

public sealed record DashboardAttendanceDto(
    DateOnly WorkDate,
    AttendanceDayStatus Status,
    DashboardShiftDto? Shift,
    [property: JsonConverter(typeof(HhMmTimeOnlyConverter))] TimeOnly? CheckIn,
    [property: JsonConverter(typeof(HhMmTimeOnlyConverter))] TimeOnly? CheckOut,
    decimal? WorkedHours,
    bool CanPunchIn,
    bool CanPunchOut,
    int? LateByMinutes);

public sealed record DashboardHolidayDto(DateOnly Date, string Name);

// ------------------------------------------------------------------ report rows

public sealed record AttendanceExceptionRow(
    string EmployeeNumber,
    string EmployeeName,
    DateOnly WorkDate,
    string Exception,
    string Detail);

// ------------------------------------------------------------------ admin dashboard & capture (admin-only)

/// <summary>Attendance + leave analytics for the admin dashboard (FR-ADM-001/041).</summary>
public sealed record AdminAttendanceDashboardDto(
    DateOnly From,
    DateOnly To,
    AdminAttendanceTotals Totals,
    IReadOnlyList<AdminAttendanceDayPoint> Daily,
    IReadOnlyList<AdminAttendanceStatusSlice> StatusBreakdown,
    IReadOnlyList<AdminLeaveTypeSlice> LeaveByType,
    IReadOnlyList<AdminDepartmentAttendance> ByDepartment);

public sealed record AdminAttendanceTotals(
    int Employees,
    int Present,
    int Absent,
    int Late,
    int HalfDay,
    int OnLeave,
    int PendingCorrections,
    decimal TotalWorkedHours,
    decimal TotalOvertimeHours);

public sealed record AdminAttendanceDayPoint(DateOnly Date, int Present, int Absent, int Late, int OnLeave);

public sealed record AdminAttendanceStatusSlice(string Status, int Count);

public sealed record AdminLeaveTypeSlice(string LeaveType, decimal Days, int Requests);

public sealed record AdminDepartmentAttendance(string Department, int Present, int Absent, int Late);

/// <summary>Admin-only captured punch record with location, selfie and timestamp.</summary>
public sealed record AdminPunchEventDto(
    Guid Id,
    Guid EmployeeId,
    string EmployeeNumber,
    string EmployeeName,
    string? Department,
    DateOnly WorkDate,
    string Direction,
    DateTime CapturedAtUtc,
    [property: JsonConverter(typeof(HhMmTimeOnlyConverter))] TimeOnly LocalTime,
    double? Latitude,
    double? Longitude,
    double? AccuracyMeters,
    bool HasPhoto,
    string Source);
