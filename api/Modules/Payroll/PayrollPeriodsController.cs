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
public sealed class PayrollPeriodsController : ControllerBase
{
    private readonly IPayrollService _payroll;

    public PayrollPeriodsController(IPayrollService payroll) => _payroll = payroll;

    /// <summary>List payroll periods (Open | Locked | Closed).</summary>
    [HttpGet]
    [Authorize]
    [Permission(Permissions.PayrollManage)]
    [ProducesResponseType(typeof(IReadOnlyList<PayrollPeriodDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PayrollPeriodDto>>> List(CancellationToken cancellationToken)
        => Ok(await _payroll.GetPeriodsAsync(cancellationToken));

    /// <summary>Create an open payroll period for importing payslips.</summary>
    [HttpPost]
    [Authorize]
    [Permission(Permissions.PayrollManage)]
    [ProducesResponseType(typeof(PayrollPeriodDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PayrollPeriodDto>> Create(
        [FromBody] CreatePayrollPeriodRequest request, CancellationToken cancellationToken)
    {
        var period = await _payroll.CreatePeriodAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, period);
    }

    /// <summary>Lock a payroll period after approval; reason required and the change is audited (FR-ADM-075).</summary>
    [HttpPost("{id:guid}/lock")]
    [Authorize]
    [Permission(Permissions.PayrollManage)]
    [ProducesResponseType(typeof(PayrollPeriodDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PayrollPeriodDto>> Lock(Guid id, [FromBody] PayrollPeriodActionRequest request, CancellationToken cancellationToken)
        => Ok(await _payroll.SetPeriodStateAsync(id, PayrollPeriodStatus.Locked, request.Reason, cancellationToken));

    /// <summary>Reopen a locked period; requires payroll.manage, a reason and is audited (FR-ADM-075).</summary>
    [HttpPost("{id:guid}/unlock")]
    [Authorize]
    [Permission(Permissions.PayrollManage)]
    [ProducesResponseType(typeof(PayrollPeriodDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PayrollPeriodDto>> Unlock(Guid id, [FromBody] PayrollPeriodActionRequest request, CancellationToken cancellationToken)
        => Ok(await _payroll.SetPeriodStateAsync(id, PayrollPeriodStatus.Open, request.Reason, cancellationToken));

    [HttpPost("{id:guid}/payslips/preview")]
    [Authorize]
    [Permission(Permissions.PayrollManage)]
    [ProducesResponseType(typeof(PayrollImportPreviewResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<PayrollImportPreviewResult>> PreviewPayslipImport(Guid id,
        [FromBody] PayrollPayslipImportRequest request, CancellationToken cancellationToken)
        => Ok(await _payroll.PreviewPayslipImportAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/payslips/import")]
    [Authorize]
    [Permission(Permissions.PayrollManage)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<IActionResult> ImportPayslips(Guid id,
        [FromBody] PayrollPayslipImportRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created,
            new { importedCount = await _payroll.ImportPayslipsAsync(id, request, cancellationToken) });

    /// <summary>
    /// Automatically calculate payslips from each employee's pay details: earnings are
    /// prorated from the annual structure, and PF/ESI/professional tax/income tax are
    /// computed. Employees without pay details or already-payslipped are skipped.
    /// </summary>
    [HttpPost("{id:guid}/payslips/generate")]
    [Authorize]
    [Permission(Permissions.PayrollManage)]
    [ProducesResponseType(typeof(PayrollGenerateResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PayrollGenerateResult>> GeneratePayslips(Guid id, CancellationToken cancellationToken)
        => Ok(await _payroll.GeneratePayslipsAsync(id, cancellationToken));

    [HttpGet("{id:guid}/payslips")]
    [Authorize]
    [Permission(Permissions.PayrollManage)]
    [ProducesResponseType(typeof(IReadOnlyList<PayrollAdminPayslipRow>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PayrollAdminPayslipRow>>> AdminPayslips(Guid id,
        CancellationToken cancellationToken)
        => Ok(await _payroll.GetAdminPayslipsAsync(id, cancellationToken));

    [HttpPost("{id:guid}/payslips/publish")]
    [Authorize]
    [Permission(Permissions.PayrollPublish)]
    [ProducesResponseType(typeof(PayrollPublishResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<PayrollPublishResult>> PublishPayslips(Guid id,
        [FromBody] PayrollPeriodActionRequest request, CancellationToken cancellationToken)
        => Ok(await _payroll.PublishPayslipsAsync(id, request.Reason, cancellationToken));
}
