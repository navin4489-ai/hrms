using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wintech.Hrms.Api.Common.ErrorHandling;
using Wintech.Hrms.Api.Common.Paging;
using Wintech.Hrms.Api.Security;
using Wintech.Hrms.Api.Security.Auth;

namespace Wintech.Hrms.Api.Modules.Admin;

/// <summary>
/// User, role, configuration, audit and operations administration — docs/API_CONTRACT.md §17
/// (FR-ADM-140..155). Every action is permission-gated; the audit log exposes no mutating verb (FR-ADM-153).
/// </summary>
[ApiController]
[Route("api/v1/admin")]
[Authorize]
[Produces("application/json")]
public sealed class AdminController : ControllerBase
{
    private readonly IAdminService _admin;

    public AdminController(IAdminService admin) => _admin = admin;

    // ---------------------------------------------------------------- users

    /// <summary>Paged user accounts with role/status/text filters (§17, FR-ADM-140).</summary>
    [HttpGet("users")]
    [Permission(Permissions.AdminUsers)]
    [ProducesResponseType(typeof(PagedResult<AdminUserListItem>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<AdminUserListItem>>> ListUsers(
        [FromQuery] string? role, [FromQuery] string? status, [FromQuery] string? q,
        [FromQuery] PageRequest page, CancellationToken cancellationToken)
        => Ok(await _admin.GetUsersAsync(role, status, q, page, cancellationToken));

    /// <summary>Create (invite) a login for an existing employee — 201 returns a one-time password.</summary>
    [HttpPost("users")]
    [Permission(Permissions.AdminUsers)]
    [ProducesResponseType(typeof(AdminUserCreatedDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminUserCreatedDto>> CreateUser([FromBody] CreateUserRequest request,
        CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await _admin.CreateUserAsync(request, cancellationToken));

    /// <summary>Replace the user's roles — audited; effective on the user's next request (UAT-007).</summary>
    [HttpPut("users/{id:guid}/roles")]
    [Permission(Permissions.AdminUsers)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateUserRoles(Guid id, [FromBody] UpdateUserRolesRequest request,
        CancellationToken cancellationToken)
    {
        await _admin.UpdateRolesAsync(id, request, cancellationToken);
        return NoContent();
    }

    /// <summary>Suspend an account — access ends immediately (BR-008), reason stored in the audit trail.</summary>
    [HttpPost("users/{id:guid}/suspend")]
    [Permission(Permissions.AdminUsers)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SuspendUser(Guid id, [FromBody] UserActionRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        await _admin.SuspendUserAsync(id, request?.Reason, cancellationToken);
        return NoContent();
    }

    /// <summary>Re-activate a suspended account.</summary>
    [HttpPost("users/{id:guid}/activate")]
    [Permission(Permissions.AdminUsers)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ActivateUser(Guid id, [FromBody] UserActionRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        await _admin.ActivateUserAsync(id, request?.Reason, cancellationToken);
        return NoContent();
    }

    /// <summary>Invalidate every outstanding refresh token for the user (remote sign-out).</summary>
    [HttpPost("users/{id:guid}/revoke-sessions")]
    [Permission(Permissions.AdminUsers)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeUserSessions(Guid id, CancellationToken cancellationToken)
    {
        await _admin.RevokeSessionsAsync(id, cancellationToken);
        return NoContent();
    }

    // ---------------------------------------------------------------- roles

    /// <summary>All roles with description, system flag, user count and permission codes (§17).</summary>
    [HttpGet("roles")]
    [Permission(Permissions.AdminRoles)]
    [ProducesResponseType(typeof(IReadOnlyList<RoleListItem>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<RoleListItem>>> ListRoles(CancellationToken cancellationToken)
        => Ok(await _admin.GetRolesAsync(cancellationToken));

    /// <summary>
    /// Replace a role's permission set — 403 for SuperAdmin or the <c>*</c> permission; every change audited.
    /// </summary>
    [HttpPut("roles/{name}/permissions")]
    [Permission(Permissions.AdminRoles)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateRolePermissions(string name,
        [FromBody] UpdateRolePermissionsRequest request, CancellationToken cancellationToken)
    {
        await _admin.UpdateRolePermissionsAsync(name, request, cancellationToken);
        return NoContent();
    }

    // ---------------------------------------------------------------- configuration

    /// <summary>All configuration keys with values, types, descriptions and last-editor (§17).</summary>
    [HttpGet("configuration")]
    [Permission(Permissions.AdminConfig)]
    [ProducesResponseType(typeof(IReadOnlyList<ConfigurationListItem>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<ConfigurationListItem>>> ListConfiguration(
        CancellationToken cancellationToken)
        => Ok(await _admin.GetConfigurationAsync(cancellationToken));

    /// <summary>Set a configuration value — typed validation; BR-012 the change applies prospectively.</summary>
    [HttpPut("configuration/{key}")]
    [Permission(Permissions.AdminConfig)]
    [ProducesResponseType(typeof(ConfigurationListItem), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ConfigurationListItem>> UpdateConfiguration(string key,
        [FromBody] UpdateConfigurationRequest request, CancellationToken cancellationToken)
        => Ok(await _admin.UpdateConfigurationAsync(key, request, cancellationToken));

    // ---------------------------------------------------------------- audit log (read-only)

    /// <summary>Search the append-only audit trail (FR-ADM-153). No write verb is exposed.</summary>
    [HttpGet("audit")]
    [Permission(Permissions.AuditView)]
    [ProducesResponseType(typeof(PagedResult<AuditListItem>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<AuditListItem>>> SearchAudit(
        [FromQuery] string? actor, [FromQuery] string? action, [FromQuery] string? entityType,
        [FromQuery] string? entityId, [FromQuery] string? from, [FromQuery] string? to,
        [FromQuery] PageRequest page, CancellationToken cancellationToken)
    {
        var fromDate = ParseDate(from, "from", cancellationToken);
        var toDate = ParseDate(to, "to", cancellationToken);
        return Ok(await _admin.GetAuditAsync(actor, action, entityType, entityId, fromDate, toDate, page, cancellationToken));
    }

    // ---------------------------------------------------------------- operations

    /// <summary>Runtime operations snapshot: background jobs, 24h failures, database size, uptime (§17).</summary>
    [HttpGet("operations")]
    [Permission(Permissions.AdminConfig)]
    [ProducesResponseType(typeof(OperationsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<OperationsDto>> Operations(CancellationToken cancellationToken)
        => Ok(await _admin.GetOperationsAsync(cancellationToken));

    // ---------------------------------------------------------------- helpers

    /// <summary>Parses an optional YYYY-MM-DD query value; 400 with the standard envelope when malformed.</summary>
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
