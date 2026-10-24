using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wintech.Hrms.Api.Common.Auditing;
using Wintech.Hrms.Api.Common.CurrentUser;
using Wintech.Hrms.Api.Common.ErrorHandling;
using Wintech.Hrms.Api.Common.Reporting;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Security;
using Wintech.Hrms.Api.Security.Auth;

namespace Wintech.Hrms.Api.Modules.Reports;

/// <summary>Report envelope metadata (§18): generation time, actor, effective filters and data scope.</summary>
public sealed record ReportMeta(DateTime GeneratedAt, string GeneratedBy, ReportFilter Filters, string Scope);

/// <summary>
/// GET /api/v1/reports/{report} — docs/API_CONTRACT.md §18. The {report} route value selects an
/// <see cref="IReportDataProvider"/> implementation (registered by whichever module owns the key);
/// unknown keys answer 404 with the contract's envelope. All reports accept from/to/departmentId/
/// locationId and carry meta: { generatedAt, generatedBy, filters, scope }. Permission: report.view.
/// </summary>
[ApiController]
[Route("api/v1/reports")]
[Authorize]
[Produces("application/json")]
public sealed class ReportsController : ControllerBase
{
    /// <summary>Full-organisation scope: report.view is granted at tenant level (BRD section 13).</summary>
    private const string ReportScope = "organization";

    private readonly IEnumerable<IReportDataProvider> _providers;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;
    private readonly IClock _clock;

    public ReportsController(IEnumerable<IReportDataProvider> providers, ICurrentUser currentUser,
        IAuditService audit, IClock clock)
    {
        _providers = providers;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
    }

    [HttpGet("{report}")]
    [Permission(Permissions.ReportView)]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReport(
        [FromRoute] string report,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] Guid? departmentId,
        [FromQuery] Guid? locationId,
        CancellationToken cancellationToken)
    {
        var provider = _providers.FirstOrDefault(p => string.Equals(p.Report, report, StringComparison.OrdinalIgnoreCase))
            ?? throw new NotFoundException($"Report '{report}' is not available.");

        var toDate = ParseDate(to, "to", cancellationToken) ?? _clock.Today;
        var fromDate = ParseDate(from, "from", cancellationToken) ?? toDate.AddYears(-1);
        if (fromDate > toDate)
            throw new RequestValidationException("The date range is not valid.",
                new Dictionary<string, string[]> { ["from"] = ["'from' must be on or before 'to'."] });

        var filter = new ReportFilter(fromDate, toDate, departmentId, locationId);
        var rows = await provider.GetRowsAsync(filter, cancellationToken);

        var generatedBy = _currentUser.DisplayName;
        if (string.IsNullOrWhiteSpace(generatedBy)) generatedBy = _currentUser.UserId.ToString();

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["rows"] = rows,
            ["meta"] = new ReportMeta(_clock.UtcNow, generatedBy, filter, ReportScope)
        };

        if (provider is IReportExtrasProvider extrasProvider)
        {
            foreach (var entry in extrasProvider.GetExtras(rows))
                payload[entry.Key] = entry.Value;
        }

        // Generating a report is an export-class read (FR-ADM-150): record who ran it with which filters.
        await _audit.RecordAsync("report.generated", "Report", report, "Success",
            new { from = fromDate, to = toDate, departmentId, locationId, rows = rows.Count },
            cancellationToken: cancellationToken);

        return Ok(payload);
    }

    private static DateOnly? ParseDate(string? value, string field, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(value)) return null;

        if (DateOnly.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return parsed;

        throw new RequestValidationException($"The '{field}' parameter is not valid.",
            new Dictionary<string, string[]> { [field] = ["The date must be in YYYY-MM-DD format."] });
    }
}
