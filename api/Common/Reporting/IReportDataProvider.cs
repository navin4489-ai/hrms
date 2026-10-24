using Wintech.Hrms.Api.Common.Registration;

namespace Wintech.Hrms.Api.Common.Reporting;

/// <summary>Shared filter for every report (docs/API_CONTRACT.md §18).</summary>
public sealed record ReportFilter(DateOnly From, DateOnly To, Guid? DepartmentId, Guid? LocationId);

/// <summary>
/// Supplies one report's row collection (docs/API_CONTRACT.md §18) without the Reports module
/// depending on the owning module's entities. Implementations are auto-registered through
/// <see cref="IAppService"/> and resolved by <see cref="Report"/>.
/// <para>
/// Known keys: <c>headcount</c>, <c>approval-aging</c>, <c>attendance-exceptions</c>,
/// <c>document-expiry</c>, <c>leave-liability</c>.
/// </para>
/// </summary>
public interface IReportDataProvider : IAppService
{
    /// <summary>Report key as it appears in <c>GET /api/v1/reports/{report}</c>.</summary>
    string Report { get; }

    /// <summary>Returns the report rows (an array-serialisable collection of row objects).</summary>
    Task<IReadOnlyList<object>> GetRowsAsync(ReportFilter filter, CancellationToken cancellationToken = default);
}
