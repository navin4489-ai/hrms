using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.Reporting;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Employees;

namespace Wintech.Hrms.Api.Modules.Reports;

/// <summary>One department group of the headcount report (contract §18: { key, label, headcount, joiners, exits }).</summary>
public sealed record HeadcountGroupRow(string Key, string Label, int Headcount, int Joiners, int Exits);

/// <summary>Headcount report totals (contract §18: { active, joiners, exits }).</summary>
public sealed record HeadcountTotals(int Active, int Joiners, int Exits);

/// <summary>
/// Headcount &amp; workforce movement grouped by department (BRD section 13):
/// <c>headcount</c> = currently active employees in the department, <c>joiners</c>/<c>exits</c>
/// = movement inside the requested date window. Department/location filters scope the query.
/// </summary>
public sealed class HeadcountReportProvider : IReportExtrasProvider
{
    private readonly AppDbContext _db;

    public HeadcountReportProvider(AppDbContext db) => _db = db;

    public string Report => "headcount";

    public async Task<IReadOnlyList<object>> GetRowsAsync(ReportFilter filter, CancellationToken cancellationToken = default)
    {
        var windowStart = filter.From.ToDateTime(TimeOnly.MinValue);
        var windowEndExclusive = filter.To.AddDays(1).ToDateTime(TimeOnly.MinValue);

        var query = _db.Employees.AsNoTracking();
        if (filter.DepartmentId is { } departmentId) query = query.Where(e => e.DepartmentId == departmentId);
        if (filter.LocationId is { } locationId) query = query.Where(e => e.LocationId == locationId);

        // Small reference table: pull the columns and group in memory (portable across SQLite/SQL Server).
        var facts = await query.Select(e => new
        {
            e.DepartmentId,
            DepartmentName = e.Department != null ? e.Department.Name : string.Empty,
            e.EmploymentStatus,
            e.JoinDate,
            ExitDate = e.LastWorkingDate ?? e.ResignationDate
        }).ToListAsync(cancellationToken);

        var groups = facts
            .GroupBy(f => f.DepartmentId)
            .Select(g =>
            {
                var label = g.Select(f => f.DepartmentName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
                            ?? "Unassigned";
                return new HeadcountGroupRow(
                    Key: g.Key.ToString(),
                    Label: label,
                    Headcount: g.Count(f => f.EmploymentStatus is EmploymentStatus.Active or EmploymentStatus.Probation),
                    Joiners: g.Count(f => f.JoinDate >= filter.From && f.JoinDate <= filter.To),
                    Exits: g.Count(f => f.ExitDate is { } exit && exit >= filter.From && exit <= filter.To));
            })
            .OrderBy(g => g.Label, StringComparer.Ordinal)
            .ToList();

        return groups.Cast<object>().ToList();
    }

    public IReadOnlyDictionary<string, object?> GetExtras(IReadOnlyList<object> rows)
    {
        var groups = rows.OfType<HeadcountGroupRow>().ToList();
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            // §18 names the payload { groups, totals }; rows carries the same collection for §18's generic envelope.
            ["groups"] = groups,
            ["totals"] = new HeadcountTotals(
                Active: groups.Sum(g => g.Headcount),
                Joiners: groups.Sum(g => g.Joiners),
                Exits: groups.Sum(g => g.Exits))
        };
    }
}
