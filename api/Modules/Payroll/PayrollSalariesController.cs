using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wintech.Hrms.Api.Security;
using Wintech.Hrms.Api.Security.Auth;

namespace Wintech.Hrms.Api.Modules.Payroll;

/// <summary>
/// Finance-maintained employee pay details (FR-ADM-070). Income tax is never entered by hand —
/// it is computed from these pay details by the tax calculator.
/// </summary>
[ApiController]
[Route("api/v1/payroll/salaries")]
[Produces("application/json")]
public sealed class PayrollSalariesController : ControllerBase
{
    private readonly IPayrollService _payroll;

    public PayrollSalariesController(IPayrollService payroll) => _payroll = payroll;

    /// <summary>All active employees with pay details and the computed annual tax / monthly TDS.</summary>
    [HttpGet]
    [Authorize]
    [Permission(Permissions.PayrollManage)]
    [ProducesResponseType(typeof(IReadOnlyList<AdminSalaryStructureDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AdminSalaryStructureDto>>> List(
        [FromQuery] string? q, CancellationToken cancellationToken)
        => Ok(await _payroll.GetSalaryStructuresAsync(q, cancellationToken));

    /// <summary>Create or replace one employee's pay details; every change is audited.</summary>
    [HttpPut("{employeeId:guid}")]
    [Authorize]
    [Permission(Permissions.PayrollManage)]
    [ProducesResponseType(typeof(AdminSalaryStructureDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminSalaryStructureDto>> Save(Guid employeeId,
        [FromBody] SaveSalaryStructureRequest request, CancellationToken cancellationToken)
        => Ok(await _payroll.SaveSalaryStructureAsync(employeeId, request, cancellationToken));

    /// <summary>
    /// Compute income tax from unsaved pay details so finance can review the tax
    /// before committing the pay record. Nothing is persisted.
    /// </summary>
    [HttpPost("preview-tax")]
    [Authorize]
    [Permission(Permissions.PayrollManage)]
    [ProducesResponseType(typeof(IncomeTaxEstimateDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<IncomeTaxEstimateDto>> PreviewTax(
        [FromBody] SaveSalaryStructureRequest request, CancellationToken cancellationToken)
        => Ok(await _payroll.PreviewTaxAsync(request, cancellationToken));
}
