using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.Reporting;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Documents;

namespace Wintech.Hrms.Api.Modules.Documents;

/// <summary>One row of GET /reports/document-expiry (contract §18 line 411).</summary>
public sealed record DocumentExpiryRow(
    string EmployeeNumber,
    string EmployeeName,
    string DocumentType,
    DateOnly? ExpiresOn,
    int DaysToExpiry,
    string Status);

/// <summary>
/// Supplies the <c>document-expiry</c> report (contract §18). Every document with an expiry
/// date on or before the filter's <c>To</c> date is listed — already expired documents stay
/// visible as overdue until HR resolves them (FR-EMP-073). Department/location filters are
/// applied through the owning employee.
/// </summary>
public sealed class DocumentExpiryReportProvider : IReportDataProvider
{
    private const int ExpiryWarningDays = 30;

    private readonly AppDbContext _db;
    private readonly IClock _clock;

    public DocumentExpiryReportProvider(AppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public string Report => "document-expiry";

    public async Task<IReadOnlyList<object>> GetRowsAsync(ReportFilter filter, CancellationToken cancellationToken = default)
    {
        var query =
            from d in _db.Set<EmployeeDocument>().AsNoTracking()
            join e in _db.Employees.AsNoTracking() on d.EmployeeId equals e.Id
            where d.ExpiresOn != null &&
                  d.ExpiresOn <= filter.To &&
                  d.Status != EmployeeDocumentStatus.Rejected &&
                  (filter.DepartmentId == null || e.DepartmentId == filter.DepartmentId) &&
                  (filter.LocationId == null || e.LocationId == filter.LocationId)
            orderby d.ExpiresOn
            select new
            {
                e.EmployeeNumber,
                EmployeeName = e.DisplayName,
                d.DocumentType,
                d.ExpiresOn,
                d.Status
            };

        var rows = await query.ToListAsync(cancellationToken);
        var today = _clock.Today;

        return rows.Select(r =>
        {
            var days = r.ExpiresOn!.Value.DayNumber - today.DayNumber;
            var status = days < 0 ? "Expired" : days <= ExpiryWarningDays ? "ExpiringSoon" : "Valid";
            return (object)new DocumentExpiryRow(
                r.EmployeeNumber, r.EmployeeName, r.DocumentType, r.ExpiresOn, days, status);
        }).ToList();
    }
}
