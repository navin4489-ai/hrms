using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wintech.Hrms.Api.Common.Paging;
using Wintech.Hrms.Api.Domain.Employees;
using Wintech.Hrms.Api.Modules.Profile;
using Wintech.Hrms.Api.Security;
using Wintech.Hrms.Api.Security.Auth;

namespace Wintech.Hrms.Api.Modules.Employees;

/// <summary>Employees & organization endpoints — contract §6 (FR-EMP-024, FR-ADM-020..027).</summary>
[ApiController]
[Route("api/v1/employees")]
[Authorize]
[Produces("application/json")]
public sealed class EmployeesController : ControllerBase
{
    private readonly IEmployeesService _employees;

    public EmployeesController(IEmployeesService employees) => _employees = employees;

    /// <summary>Organization directory — any authenticated user; personal contact details never exposed (FR-EMP-024).</summary>
    [HttpGet("directory")]
    [ProducesResponseType(typeof(PagedResult<DirectoryItemDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<DirectoryItemDto>>> Directory(
        [FromQuery] string? q, [FromQuery] Guid? departmentId, [FromQuery] Guid? locationId,
        [FromQuery] string? sort, [FromQuery] PageRequest page, CancellationToken cancellationToken)
        => Ok(await _employees.GetDirectoryAsync(q, departmentId, locationId, sort, page, cancellationToken));

    /// <summary>Admin list of employee records (employee.view; filters q/status/departmentId/locationId).</summary>
    [HttpGet]
    [Permission(Permissions.EmployeeView)]
    [ProducesResponseType(typeof(PagedResult<EmployeeListItem>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<EmployeeListItem>>> List(
        [FromQuery] string? q, [FromQuery] EmploymentStatus? status,
        [FromQuery] Guid? departmentId, [FromQuery] Guid? locationId,
        [FromQuery] PageRequest page, CancellationToken cancellationToken)
        => Ok(await _employees.GetListAsync(q, status, departmentId, locationId, page, cancellationToken));

    /// <summary>Own record, or any record with employee.view. Sensitive fields masked unless
    /// document.sensitive + ?unmask=true (then the read is audited).</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(EmployeeProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EmployeeProfileDto>> Detail(Guid id, [FromQuery] bool unmask = false,
        CancellationToken cancellationToken = default)
        => Ok(await _employees.GetDetailAsync(id, unmask, cancellationToken));

    /// <summary>Effective-dated employment history (employee.view).</summary>
    [HttpGet("{id:guid}/history")]
    [Permission(Permissions.EmployeeView)]
    [ProducesResponseType(typeof(IReadOnlyList<EmploymentHistoryItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<EmploymentHistoryItemDto>>> History(Guid id,
        CancellationToken cancellationToken)
        => Ok(await _employees.GetHistoryAsync(id, cancellationToken));

    /// <summary>Create an employee (employee.create; 409 on duplicate number/email).</summary>
    [HttpPost]
    [Permission(Permissions.EmployeeCreate)]
    [ProducesResponseType(typeof(EmployeeProfileDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<EmployeeProfileDto>> Create([FromBody] CreateEmployeeRequest request,
        CancellationToken cancellationToken)
    {
        var created = await _employees.CreateAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    /// <summary>Edit an employee record (employee.edit; payroll/reporting fields write history, FR-ADM-026).</summary>
    [HttpPut("{id:guid}")]
    [Permission(Permissions.EmployeeEdit)]
    [ProducesResponseType(typeof(EmployeeProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<EmployeeProfileDto>> Update(Guid id, [FromBody] UpdateEmployeeRequest request,
        CancellationToken cancellationToken)
        => Ok(await _employees.UpdateAsync(id, request, cancellationToken));

    /// <summary>Suspend/reactivate/terminate (employee.manage; BR-008 access revocation).</summary>
    [HttpPost("{id:guid}/status")]
    [Permission(Permissions.EmployeeManage)]
    [ProducesResponseType(typeof(EmployeeProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<EmployeeProfileDto>> ChangeStatus(Guid id,
        [FromBody] ChangeEmployeeStatusRequest request, CancellationToken cancellationToken)
        => Ok(await _employees.ChangeStatusAsync(id, request, cancellationToken));
}
