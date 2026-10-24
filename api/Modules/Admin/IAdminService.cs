using Wintech.Hrms.Api.Common.Paging;
using Wintech.Hrms.Api.Common.Registration;

namespace Wintech.Hrms.Api.Modules.Admin;

/// <summary>
/// User, role, configuration, audit and operations administration (docs/API_CONTRACT.md §17,
/// FR-ADM-140..155). Every mutation is written to the append-only audit trail atomically with
/// the change itself; audit reads are recorded with <c>IAuditService.RecordAsync</c> (FR-ADM-153).
/// </summary>
public interface IAdminService : IAppService
{
    Task<PagedResult<AdminUserListItem>> GetUsersAsync(string? role, string? status, string? q,
        PageRequest page, CancellationToken cancellationToken = default);

    Task<AdminUserCreatedDto> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default);

    /// <summary>Replaces the user's role set; takes effect on the user's next request (UAT-007).</summary>
    Task UpdateRolesAsync(Guid userId, UpdateUserRolesRequest request, CancellationToken cancellationToken = default);

    Task SuspendUserAsync(Guid userId, string? reason, CancellationToken cancellationToken = default);

    Task ActivateUserAsync(Guid userId, string? reason, CancellationToken cancellationToken = default);

    /// <summary>Invalidates every outstanding refresh token for the user (sessions end immediately).</summary>
    Task RevokeSessionsAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RoleListItem>> GetRolesAsync(CancellationToken cancellationToken = default);

    /// <summary>403 when editing SuperAdmin or granting the <c>*</c> permission (contract §17).</summary>
    Task UpdateRolePermissionsAsync(string name, UpdateRolePermissionsRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ConfigurationListItem>> GetConfigurationAsync(CancellationToken cancellationToken = default);

    /// <summary>200 with the updated entry; BR-012 — the change applies prospectively.</summary>
    Task<ConfigurationListItem> UpdateConfigurationAsync(string key, UpdateConfigurationRequest request,
        CancellationToken cancellationToken = default);

    Task<PagedResult<AuditListItem>> GetAuditAsync(string? actor, string? action, string? entityType,
        string? entityId, DateOnly? from, DateOnly? to, PageRequest page, CancellationToken cancellationToken = default);

    Task<OperationsDto> GetOperationsAsync(CancellationToken cancellationToken = default);
}
