using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wintech.Hrms.Api.Common.Paging;
using Wintech.Hrms.Api.Security;
using Wintech.Hrms.Api.Security.Auth;

namespace Wintech.Hrms.Api.Modules.Leave;

/// <summary>Leave reference data: types, balances and policies (docs/API_CONTRACT.md §8, FR-EMP-040).</summary>
[ApiController]
[Authorize]
[Produces("application/json")]
public sealed class LeaveReferenceController : ControllerBase
{
    private readonly ILeaveService _leave;

    public LeaveReferenceController(ILeaveService leave) => _leave = leave;

    /// <summary>Configured leave types (FR-ADM-050).</summary>
    [HttpGet("api/v1/leave-types")]
    [ProducesResponseType(typeof(IReadOnlyList<LeaveTypeDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<LeaveTypeDto>>> Types(CancellationToken cancellationToken)
        => Ok(await _leave.GetTypesAsync(cancellationToken));

    /// <summary>The caller's balances for a year (FR-EMP-040).</summary>
    [HttpGet("api/v1/leave-balances")]
    [ProducesResponseType(typeof(IReadOnlyList<LeaveBalanceDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<LeaveBalanceDto>>> Balances([FromQuery] int? year,
        CancellationToken cancellationToken)
        => Ok(await _leave.GetBalancesAsync(year, cancellationToken));

    /// <summary>Active leave policies (accrual, carry-forward, notice and attachment rules).</summary>
    [HttpGet("api/v1/leave-policies")]
    [ProducesResponseType(typeof(IReadOnlyList<LeavePolicyDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<LeavePolicyDto>>> Policies(CancellationToken cancellationToken)
        => Ok(await _leave.GetPoliciesAsync(cancellationToken));

    [HttpGet("api/v1/admin/leave-types")]
    [Permission(Permissions.AdminConfig)]
    public async Task<ActionResult<IReadOnlyList<AdminLeaveTypeDto>>> AdminTypes(CancellationToken cancellationToken)
        => Ok(await _leave.GetAdminLeaveTypesAsync(cancellationToken));

    [HttpPost("api/v1/admin/leave-types")]
    [Permission(Permissions.AdminConfig)]
    [ProducesResponseType(typeof(AdminLeaveTypeDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<AdminLeaveTypeDto>> CreateType([FromBody] UpsertLeaveTypeRequest request,
        CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created,
            await _leave.UpsertLeaveTypeAsync(null, request, cancellationToken));

    [HttpPut("api/v1/admin/leave-types/{id:guid}")]
    [Permission(Permissions.AdminConfig)]
    public async Task<ActionResult<AdminLeaveTypeDto>> UpdateType(Guid id,
        [FromBody] UpsertLeaveTypeRequest request, CancellationToken cancellationToken)
        => Ok(await _leave.UpsertLeaveTypeAsync(id, request, cancellationToken));

    [HttpGet("api/v1/admin/leave-policies")]
    [Permission(Permissions.AdminConfig)]
    public async Task<ActionResult<IReadOnlyList<AdminLeavePolicyDto>>> AdminPolicies(CancellationToken cancellationToken)
        => Ok(await _leave.GetAdminLeavePoliciesAsync(cancellationToken));

    [HttpPost("api/v1/admin/leave-policies")]
    [Permission(Permissions.AdminConfig)]
    [ProducesResponseType(typeof(AdminLeavePolicyDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<AdminLeavePolicyDto>> CreatePolicy([FromBody] UpsertLeavePolicyRequest request,
        CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created,
            await _leave.UpsertLeavePolicyAsync(null, request, cancellationToken));

    [HttpPut("api/v1/admin/leave-policies/{id:guid}")]
    [Permission(Permissions.AdminConfig)]
    public async Task<ActionResult<AdminLeavePolicyDto>> UpdatePolicy(Guid id,
        [FromBody] UpsertLeavePolicyRequest request, CancellationToken cancellationToken)
        => Ok(await _leave.UpsertLeavePolicyAsync(id, request, cancellationToken));

    /// <summary>Admin/HR: view an employee's leave balances for a year (FR-ADM-054).</summary>
    [HttpGet("api/v1/admin/leave/balances")]
    [Permission(Permissions.LeaveManage)]
    [ProducesResponseType(typeof(IReadOnlyList<LeaveBalanceDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<LeaveBalanceDto>>> EmployeeBalances(
        [FromQuery] Guid employeeId, [FromQuery] int? year, CancellationToken cancellationToken)
        => Ok(await _leave.GetEmployeeBalancesAsync(employeeId, year, cancellationToken));

    /// <summary>Admin/HR: add/credit (or set) an employee's leave balance (FR-ADM-054).</summary>
    [HttpPost("api/v1/admin/leave/balances/adjust")]
    [Permission(Permissions.LeaveManage)]
    [ProducesResponseType(typeof(LeaveBalanceAdjustedDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LeaveBalanceAdjustedDto>> AdjustBalance(
        [FromBody] AdjustLeaveBalanceRequest request, CancellationToken cancellationToken)
        => Ok(await _leave.AdjustBalanceAsync(request, cancellationToken));
}

/// <summary>Leave requests: submit, list, detail and cancel (docs/API_CONTRACT.md §8, FR-EMP-041..044).</summary>
[ApiController]
[Route("api/v1/leave-requests")]
[Authorize]
[Produces("application/json")]
public sealed class LeaveRequestsController : ControllerBase
{
    private readonly ILeaveService _leave;

    public LeaveRequestsController(ILeaveService leave) => _leave = leave;

    /// <summary>
    /// Submit a leave request (FR-EMP-041/042). 201 with `approvalTaskId`; 422 for balance,
    /// overlap, weekend/holiday-only, notice or attachment violations.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(LeaveRequestCreatedDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<LeaveRequestCreatedDto>> Create([FromBody] LeaveRequestCreateDto request,
        CancellationToken cancellationToken)
    {
        var created = await _leave.CreateRequestAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    /// <summary>
    /// List leave requests (own; `team=true` needs `leave.approve`/`attendance.view.team`;
    /// filters: status, year, month, team, employeeId — paged).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<LeaveRequestListItem>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<LeaveRequestListItem>>> List(
        [FromQuery] string? status, [FromQuery] int? year, [FromQuery] int? month,
        [FromQuery] bool team, [FromQuery] Guid? employeeId, [FromQuery] PageRequest page,
        CancellationToken cancellationToken)
        => Ok(await _leave.ListRequestsAsync(status, year, month, team, employeeId, page, cancellationToken));

    /// <summary>Request detail including `decisionHistory` (FR-EMP-043).</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(LeaveRequestDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LeaveRequestDetail>> GetDetail([FromRoute] Guid id, CancellationToken cancellationToken)
        => Ok(await _leave.GetRequestAsync(id, cancellationToken));

    /// <summary>
    /// Cancel a pending request (FR-EMP-044). 409 when already decided or outside the
    /// configured cancellation window.
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(typeof(LeaveCancelledDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LeaveCancelledDto>> Cancel([FromRoute] Guid id, [FromBody] LeaveCancelDto request,
        CancellationToken cancellationToken)
        => Ok(await _leave.CancelRequestAsync(id, request.Reason, cancellationToken));
}

/// <summary>Personal leave/holiday/week-off calendar (docs/API_CONTRACT.md §8, FR-EMP-045).</summary>
[ApiController]
[Route("api/v1/leave/calendar")]
[Authorize]
[Produces("application/json")]
public sealed class LeaveCalendarController : ControllerBase
{
    private readonly ILeaveService _leave;

    public LeaveCalendarController(ILeaveService leave) => _leave = leave;

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<LeaveCalendarEntry>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<LeaveCalendarEntry>>> Get(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken cancellationToken)
        => Ok(await _leave.GetCalendarAsync(from, to, cancellationToken));
}
