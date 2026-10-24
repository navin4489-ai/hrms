using Wintech.Hrms.Api.Common.Reporting;

namespace Wintech.Hrms.Api.Modules.Reports;

/// <summary>
/// Optional add-on for reports whose contract payload carries top-level fields in addition to
/// <c>rows</c>/<c>meta</c> (docs/API_CONTRACT.md §18: the headcount report responds with
/// <c>{ groups, totals }</c>). Implemented alongside <see cref="IReportDataProvider"/> — the extra
/// fields are derived from the rows already fetched, so no second query runs.
/// </summary>
public interface IReportExtrasProvider : IReportDataProvider
{
    /// <summary>Returns additional top-level payload entries (e.g. "groups", "totals") for the fetched rows.</summary>
    IReadOnlyDictionary<string, object?> GetExtras(IReadOnlyList<object> rows);
}
