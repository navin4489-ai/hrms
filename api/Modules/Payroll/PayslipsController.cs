using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Wintech.Hrms.Api.Modules.Payroll;

/// <summary>Payslip self-service (contract §10, FR-EMP-060..064). Own records only unless payslip.view.any.</summary>
[ApiController]
[Route("api/v1/payslips")]
[Produces("application/json")]
public sealed class PayslipsController : ControllerBase
{
    private readonly IPayrollService _payroll;

    public PayslipsController(IPayrollService payroll) => _payroll = payroll;

    /// <summary>List the caller's published payslips, optionally filtered by year (UAT-006: own records only).</summary>
    [HttpGet]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<PayslipListItem>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PayslipListItem>>> List(
        [FromQuery] int? year,
        [FromQuery] Guid? employeeId,
        CancellationToken cancellationToken)
        => Ok(await _payroll.GetPayslipsAsync(year, employeeId, cancellationToken));

    /// <summary>Full earnings/deductions breakdown of one payslip (404 for another employee's payslip without payslip.view.any).</summary>
    [HttpGet("{id:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(PayslipDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PayslipDetailDto>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await _payroll.GetPayslipAsync(id, cancellationToken));

    /// <summary>Download the payslip as a server-generated PDF; access is authenticated and audited (FR-EMP-062).</summary>
    [HttpGet("{id:guid}/download")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var pdf = await _payroll.GeneratePayslipPdfAsync(id, cancellationToken);
        return File(pdf.Content, "application/pdf", pdf.FileName);
    }
}
