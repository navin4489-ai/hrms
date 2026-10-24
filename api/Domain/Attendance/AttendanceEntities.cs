using Wintech.Hrms.Api.Domain.Employees;

namespace Wintech.Hrms.Api.Domain.Attendance;

/// <summary>Daily attendance state as rendered on calendars (docs/API_CONTRACT.md §5).</summary>
public enum AttendanceDayStatus
{
    NotMarked = 0,
    Present = 1,
    Absent = 2,
    Late = 3,
    HalfDay = 4,
    MissingPunch = 5,
    WeekOff = 6,
    Holiday = 7,
    Leave = 8
}

/// <summary>How a punch or record entered the system (FR-EMP-031).</summary>
public enum AttendanceSource
{
    Mobile = 0,
    Web = 1,
    Biometric = 2,
    Import = 3,
    Admin = 4
}

/// <summary>Kind of attendance correction requested (FR-EMP-035).</summary>
public enum AttendanceCorrectionType
{
    MissingPunch = 0,
    IncorrectCheckIn = 1,
    IncorrectCheckOut = 2,
    IncorrectTimes = 3,
    ShiftCorrection = 4,
    Overtime = 5,
    Other = 6
}

public enum AttendanceCorrectionStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Returned = 3,
    Cancelled = 4
}

public enum ShiftSwapStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Returned = 3,
    Cancelled = 4
}

/// <summary>Holiday classification (FR-ADM-055).</summary>
public enum HolidayKind
{
    Public = 0,
    Optional = 1,
    Shutdown = 2
}

/// <summary>Configured shift definition (FR-ADM-040): general, early-bird, support, night…</summary>
public class WorkShift
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public int GraceMinutes { get; set; }
    public int BreakMinutes { get; set; }
    /// <summary>Comma-separated weekday names the shift normally covers, e.g. "Mon,Tue,Wed,Thu,Fri".</summary>
    public string WeekDays { get; set; } = "Mon,Tue,Wed,Thu,Fri";
    /// <summary>Fallback shift applied when an employee has no explicit roster entry for a day.</summary>
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Roster: the shift an employee is assigned to on a given date (FR-EMP-050/051).</summary>
public class ShiftAssignment
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public DateOnly Date { get; set; }
    public Guid ShiftId { get; set; }
    public WorkShift? Shift { get; set; }
    public string? Note { get; set; }
}

/// <summary>One employee's attendance for one work date (FR-EMP-030..038, FR-ADM-041/044).</summary>
public class AttendanceDayRecord
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public DateOnly WorkDate { get; set; }
    public Guid? ShiftId { get; set; }
    public WorkShift? Shift { get; set; }
    /// <summary>Check-in time in organisation-local business time (Asia/Kolkata).</summary>
    public TimeOnly? CheckIn { get; set; }
    /// <summary>Check-out time in organisation-local business time (Asia/Kolkata).</summary>
    public TimeOnly? CheckOut { get; set; }
    public AttendanceDayStatus Status { get; set; } = AttendanceDayStatus.NotMarked;
    public int LateByMinutes { get; set; }
    public decimal? WorkedHours { get; set; }
    public decimal OvertimeHours { get; set; }
    public bool IsHalfDay { get; set; }
    public AttendanceSource Source { get; set; } = AttendanceSource.Mobile;
    public DateTime UpdatedAt { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// Employee-submitted correction of missed/incorrect punches or shift (FR-EMP-035..037).
/// Original values are snapshotted at submission and preserved when the correction is
/// approved (FR-ADM-044, UAT-005).
/// </summary>
public class AttendanceCorrectionRequest
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public DateOnly WorkDate { get; set; }
    public AttendanceCorrectionType Type { get; set; }
    public TimeOnly? ProposedCheckIn { get; set; }
    public TimeOnly? ProposedCheckOut { get; set; }
    /// <summary>Snapshot of the record's check-in at submission time (never overwritten).</summary>
    public TimeOnly? OriginalCheckIn { get; set; }
    /// <summary>Snapshot of the record's check-out at submission time (never overwritten).</summary>
    public TimeOnly? OriginalCheckOut { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? AttachmentUrl { get; set; }
    public AttendanceCorrectionStatus Status { get; set; } = AttendanceCorrectionStatus.Pending;
    public Guid? ApprovalTaskId { get; set; }
    public DateTime SubmittedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionComment { get; set; }
    public Guid CreatedByUserId { get; set; }
}

/// <summary>Request to swap rostered shifts with a colleague for one date (FR-EMP-051).</summary>
public class ShiftSwapRequest
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public DateOnly RosterDate { get; set; }
    public Guid ProposedWithEmployeeId { get; set; }
    /// <summary>Optional specific shift the requester wants; when null the two rostered shifts are exchanged.</summary>
    public Guid? ProposedShiftId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public ShiftSwapStatus Status { get; set; } = ShiftSwapStatus.Pending;
    public Guid? ApprovalTaskId { get; set; }
    public DateTime SubmittedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionComment { get; set; }
    /// <summary>Roster changes applied exactly once on approval (UAT-004).</summary>
    public bool Applied { get; set; }
    public Guid CreatedByUserId { get; set; }
}

/// <summary>Published holiday / optional holiday / shutdown day (FR-ADM-055, FR-EMP-050).</summary>
public class CompanyHoliday
{
    public Guid Id { get; set; }
    public DateOnly Date { get; set; }
    public string Name { get; set; } = string.Empty;
    public HolidayKind Type { get; set; } = HolidayKind.Public;
    /// <summary>Null = company-wide holiday; otherwise only applies at that location.</summary>
    public Guid? LocationId { get; set; }
}
