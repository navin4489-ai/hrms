using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.Reporting;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Attendance;
using Wintech.Hrms.Api.Domain.Employees;
using Wintech.Hrms.Api.Domain.Leave;

namespace Wintech.Hrms.Api.Modules.Attendance;

/// <summary>
/// GET /reports/attendance-exceptions rows (docs/API_CONTRACT.md §18, FR-ADM-041):
/// MissingPunch | Late | Absent | PendingCorrection within the requested range.
/// </summary>
public sealed class AttendanceExceptionsReportProvider : IReportDataProvider
{
    private readonly AppDbContext _db;
    private readonly IClock _clock;

    public AttendanceExceptionsReportProvider(AppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public string Report => "attendance-exceptions";

    public async Task<IReadOnlyList<object>> GetRowsAsync(ReportFilter filter, CancellationToken cancellationToken = default)
    {
        var today = AttendanceTimeHelper.DateLocal(_clock.UtcNow);
        var to = filter.To > today ? today : filter.To;
        if (to < filter.From) return [];

        var employees = await EmployeeScope(filter, cancellationToken);
        if (employees.Count == 0) return [];
        var employeeIds = employees.Keys.ToList();

        var rows = new List<AttendanceExceptionRow>();

        // MissingPunch / Late — from recorded punches.
        var records = await _db.Set<AttendanceDayRecord>().AsNoTracking()
            .Where(r => employeeIds.Contains(r.EmployeeId) && r.WorkDate >= filter.From && r.WorkDate <= to)
            .ToListAsync(cancellationToken);

        foreach (var record in records)
        {
            if (!employees.TryGetValue(record.EmployeeId, out var employee)) continue;

            if (record.CheckIn is not null && record.CheckOut is null && record.WorkDate < today)
            {
                rows.Add(new AttendanceExceptionRow(employee.EmployeeNumber, employee.DisplayName, record.WorkDate,
                    "MissingPunch", $"Checked in at {record.CheckIn:HH:mm} without a check-out."));
            }

            if (record.LateByMinutes > 0)
            {
                rows.Add(new AttendanceExceptionRow(employee.EmployeeNumber, employee.DisplayName, record.WorkDate,
                    "Late", $"{record.LateByMinutes} minute(s) late against the rostered shift start."));
            }
        }

        // Absent — rostered working days with no record and no approved leave.
        var assignments = await _db.Set<ShiftAssignment>().AsNoTracking()
            .Where(a => employeeIds.Contains(a.EmployeeId) && a.Date >= filter.From && a.Date <= to && a.Date < today)
            .Select(a => new { a.EmployeeId, a.Date })
            .ToListAsync(cancellationToken);

        var approvedLeave = await _db.Set<LeaveRequest>().AsNoTracking()
            .Where(r => employeeIds.Contains(r.EmployeeId) && r.Status == LeaveRequestStatus.Approved
                        && r.StartDate <= to && r.EndDate >= filter.From)
            .Select(r => new { r.EmployeeId, r.StartDate, r.EndDate })
            .ToListAsync(cancellationToken);

        var recordKeys = records
            .Where(r => r.CheckIn is not null || r.CheckOut is not null)
            .Select(r => (r.EmployeeId, r.WorkDate))
            .ToHashSet();

        foreach (var assignment in assignments)
        {
            if (recordKeys.Contains((assignment.EmployeeId, assignment.Date))) continue;
            var onLeave = approvedLeave.Any(l => l.EmployeeId == assignment.EmployeeId
                                                  && assignment.Date >= l.StartDate && assignment.Date <= l.EndDate);
            if (onLeave) continue;
            if (!employees.TryGetValue(assignment.EmployeeId, out var employee)) continue;

            rows.Add(new AttendanceExceptionRow(employee.EmployeeNumber, employee.DisplayName, assignment.Date,
                "Absent", "No attendance recorded for a rostered working day."));
        }

        // PendingCorrection — corrections awaiting a decision.
        var pendingCorrections = await _db.Set<AttendanceCorrectionRequest>().AsNoTracking()
            .Where(c => employeeIds.Contains(c.EmployeeId) && c.Status == AttendanceCorrectionStatus.Pending
                        && c.WorkDate >= filter.From && c.WorkDate <= to)
            .ToListAsync(cancellationToken);

        foreach (var correction in pendingCorrections)
        {
            if (!employees.TryGetValue(correction.EmployeeId, out var employee)) continue;
            rows.Add(new AttendanceExceptionRow(employee.EmployeeNumber, employee.DisplayName, correction.WorkDate,
                "PendingCorrection", $"{correction.Type} correction awaiting approval: {correction.Reason}"));
        }

        return rows
            .OrderByDescending(r => r.WorkDate)
            .ThenBy(r => r.EmployeeNumber)
            .ThenBy(r => r.Exception)
            .Select(r => (object)r)
            .ToList();
    }

    /// <summary>Employees narrowed by department/location filters -> (id, number, name).</summary>
    private async Task<Dictionary<Guid, (string EmployeeNumber, string DisplayName)>> EmployeeScope(
        ReportFilter filter, CancellationToken cancellationToken)
    {
        var query = _db.Employees.AsNoTracking()
            .Where(e => e.EmploymentStatus != EmploymentStatus.Terminated);
        if (filter.DepartmentId is { } departmentId) query = query.Where(e => e.DepartmentId == departmentId);
        if (filter.LocationId is { } locationId) query = query.Where(e => e.LocationId == locationId);

        var list = await query
            .Select(e => new { e.Id, e.EmployeeNumber, e.DisplayName })
            .ToListAsync(cancellationToken);
        return list.ToDictionary(e => e.Id, e => (e.EmployeeNumber, e.DisplayName));
    }
}
