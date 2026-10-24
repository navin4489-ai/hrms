using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wintech.Hrms.Api.Domain.Payroll;
using Wintech.Hrms.Api.Security;
using Wintech.Hrms.Api.Security.Auth;

namespace Wintech.Hrms.Api.Modules.Payroll;

/// <summary>Payroll period controls (contract §10, FR-ADM-071/075) — payroll.manage only.</summary>
[ApiController]
[Route("api/v1/payroll-periods")]
[Produces("application/json")]
[Permission(Permissions.PayrollManage)]
public sealed class PayrollPeriodsController : ControllerBase
{
    private readonly IPayrollService _payroll;

    public PayrollPeriodsController(IPayrollService payroll) => _payroll = payroll;

    /// <summary>List payroll periods (Open | Locked | Closed).</summary>
    [HttpGet]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<PayrollPeriodDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PayrollPeriodDto>>> List(CancellationToken cancellationToken)
        => Ok(await _payroll.GetPeriodsAsync(cancellationToken));

    /// <summary>Lock a payroll period after approval; reason required and the change is audited (FR-ADM-075).</summary>
    [HttpPost("{id:guid}/lock")]
    [Authorize]
    [ProducesResponseType(typeof(PayrollPeriodDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PayrollPeriodDto>> Lock(Guid id, [FromBody] PayrollPeriodActionRequest request, CancellationToken cancellationToken)
        => Ok(await _payroll.SetPeriodStateAsync(id, PayrollPeriodStatus.Locked, request.Reason, cancellationToken));

    /// <summary>Reopen a locked period; requires payroll.manage, a reason and is audited (FR-ADM-075).</summary>
    [HttpPost("{id:guid}/unlock")]
    [Authorize]
    [ProducesResponseType(typeof(PayrollPeriodDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PayrollPeriodDto>> Unlock(Guid id, [FromBody] PayrollPeriodActionRequest request, CancellationToken cancellationToken)
        => Ok(await _payroll.SetPeriodStateAsync(id, PayrollPeriodStatus.Open, request.Reason, cancellationToken));
}
