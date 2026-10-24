using System.ComponentModel.DataAnnotations;
using Wintech.Hrms.Api.Domain.Identity;

namespace Wintech.Hrms.Api.Modules.Admin;

// ---------------------------------------------------------------- GET /admin/users

/// <summary>Contract shape for GET /api/v1/admin/users items (§17).</summary>
public class AdminUserListItem
{
    public Guid Id { get; init; }
    public Guid? EmployeeId { get; init; }
    public string EmployeeNumber { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? WorkEmail { get; init; }
    public string[] Roles { get; init; } = [];
    public UserStatus Status { get; init; }
    public bool MfaEnabled { get; init; }
    public DateTime? LastLoginAt { get; init; }
    public int SessionsActive { get; init; }
}

/// <summary>201 body for POST /api/v1/admin/users — the account plus its one-time password.</summary>
public sealed class AdminUserCreatedDto : AdminUserListItem
{
    /// <summary>Returned exactly once at creation (never persisted in plain text nor audited).</summary>
    public string TemporaryPassword { get; init; } = string.Empty;
}

// ---------------------------------------------------------------- request bodies

public sealed class CreateUserRequest
{
    [Required(ErrorMessage = "An employee id is required.")]
    public Guid? EmployeeId { get; init; }

    /// <summary>Role names to grant; may be an empty array but must be present.</summary>
    [Required(ErrorMessage = "A roles list is required (it may be empty).")]
    public string[]? Roles { get; init; }
}

public sealed class UpdateUserRolesRequest
{
    [Required(ErrorMessage = "A roles list is required (it may be empty).")]
    public string[]? Roles { get; init; }
}

/// <summary>Optional body for suspend/activate: <c>{ "reason": "..." }</c>.</summary>
public sealed class UserActionRequest
{
    [StringLength(500, ErrorMessage = "The reason may not exceed 500 characters.")]
    public string? Reason { get; init; }
}

public sealed class UpdateRolePermissionsRequest
{
    [Required(ErrorMessage = "A permissions list is required (it may be empty).")]
    public string[]? Permissions { get; init; }
}

public sealed class UpdateConfigurationRequest
{
    [Required(AllowEmptyStrings = true, ErrorMessage = "A value is required.")]
    public string? Value { get; init; }
}

// ---------------------------------------------------------------- other responses

/// <summary>Contract shape for GET /api/v1/admin/roles items (§17).</summary>
public sealed class RoleListItem
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool IsSystem { get; init; }
    public int UserCount { get; init; }
    public string[] Permissions { get; init; } = [];
}

/// <summary>Contract shape for GET /api/v1/admin/configuration items (§17).</summary>
public sealed class ConfigurationListItem
{
    public string Key { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string Type { get; init; } = "String";
    public string Description { get; init; } = string.Empty;
    public DateTime? UpdatedAt { get; init; }
    public string? UpdatedByName { get; init; }
}

/// <summary>Contract shape for GET /api/v1/admin/audit items (§17) — read-only (FR-ADM-153).</summary>
public sealed class AuditListItem
{
    public Guid Id { get; init; }
    public string ActorName { get; init; } = string.Empty;
    public string? ActorEmployeeNumber { get; init; }
    public string Action { get; init; } = string.Empty;
    public string? EntityType { get; init; }
    public string? EntityId { get; init; }
    public string Outcome { get; init; } = "Success";
    public string? CorrelationId { get; init; }
    public string? IpAddress { get; init; }
    public DateTime Timestamp { get; init; }
    public string? DetailsJson { get; init; }
}

/// <summary>Contract shape for GET /api/v1/admin/operations (§17, FR-ADM-004/154).</summary>
public sealed class OperationsDto
{
    public IReadOnlyList<BackgroundJobDto> BackgroundJobs { get; init; } = [];
    /// <summary>Null when the API host has no source of truth for the figure yet.</summary>
    public int? NotificationFailures24h { get; init; }
    /// <summary>Null when the API host has no source of truth for the figure yet.</summary>
    public int? IntegrationErrors24h { get; init; }
    /// <summary>Null for providers that do not store the database as a local file.</summary>
    public double? DbSizeMb { get; init; }
    public int UptimeMinutes { get; init; }
}

public sealed class BackgroundJobDto
{
    public string Name { get; init; } = string.Empty;
    public DateTime? LastRunAt { get; init; }
    public string Status { get; init; } = "Unknown";
}
