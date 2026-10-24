using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.Reporting;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Employees;
using Wintech.Hrms.Api.Domain.Leave;

namespace Wintech.Hrms.Api.Modules.Leave;

/// <summary>
/// GET /reports/leave-liability rows (docs/API_CONTRACT.md §18): one row per department and
/// leave type, aggregated from the year covered by the report filter.
/// </summary>
public sealed class LeaveLiabilityReportProvider : IReportDataProvider
{
    private readonly AppDbContext _db;

    public LeaveLiabilityReportProvider(AppDbContext db)
    {
        _db = db;
    }

    public string Report => "leave-liability";

    public async Task<IReadOnlyList<object>> GetRowsAsync(ReportFilter filter, CancellationToken cancellationToken = default)
    {
        var year = filter.From.Year;
        var employeeQuery = _db.Employees.AsNoTracking()
            .Where(e => e.EmploymentStatus != EmploymentStatus.Terminated);
        if (filter.DepartmentId is { } departmentId) employeeQuery = employeeQuery.Where(e => e.DepartmentId == departmentId);
        if (filter.LocationId is { } locationId) employeeQuery = employeeQuery.Where(e => e.LocationId == locationId);

        var employees = await employeeQuery
            .Select(e => new
            {
                e.Id,
                Department = e.Department != null ? e.Department.Name : "Unassigned"
            })
            .ToListAsync(cancellationToken);
        if (employees.Count == 0) return [];

        var departmentByEmployee = employees.ToDictionary(e => e.Id, e => e.Department);

        var balances = await _db.Set<LeaveBalance>().AsNoTracking()
            .Where(b => b.Year == year && departmentByEmployee.Keys.Contains(b.EmployeeId))
            .Select(b => new
            {
                b.EmployeeId, b.LeaveTypeId, b.Accrued, b.Used, b.Pending, b.CarryForward
            })
            .ToListAsync(cancellationToken);
        if (balances.Count == 0) return [];

        var typeNames = await _db.Set<LeaveType>().AsNoTracking()
            .ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);

        // Aggregate in memory: SQLite decimal ordering/aggregation in SQL is unreliable.
        var groups = balances
            .GroupBy(b => (departmentByEmployee.GetValueOrDefault(b.EmployeeId, "Unassigned"),
                typeNames.GetValueOrDefault(b.LeaveTypeId, "Unknown")))
            .Select(g => (object)new LeaveLiabilityRow(
                g.Key.Item1,
                g.Key.Item2,
                g.Sum(b => b.Accrued) + g.Sum(b => b.CarryForward),
                g.Sum(b => b.Used),
                g.Sum(b => b.Pending),
                g.Sum(b => b.CarryForward)))
            .OrderBy(r => ((LeaveLiabilityRow)r).Department)
            .ThenBy(r => ((LeaveLiabilityRow)r).LeaveType)
            .ToList();

        return groups;
    }
}
