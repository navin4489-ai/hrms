using Wintech.Hrms.Api.Common.Registration;

namespace Wintech.Hrms.Api.Modules.Organizations;

/// <summary>
/// SuperAdmin organization administration (FR-ADM-001/002): create and configure organizations,
/// per-org module flags and employee-code settings. All reads/writes run with the tenant filter
/// bypassed because only the tenant owner (SuperAdmin) reaches these endpoints.
/// </summary>
public interface IOrganizationService : IAppService
{
    Task<IReadOnlyList<OrganizationDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<OrganizationDetailDto> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<OrganizationDetailDto> CreateAsync(CreateOrganizationRequest request, CancellationToken cancellationToken = default);
    Task<OrganizationDetailDto> UpdateAsync(Guid id, UpdateOrganizationRequest request, CancellationToken cancellationToken = default);
    Task<OrganizationDetailDto> UpdateModulesAsync(Guid id, UpdateOrgModulesRequest request, CancellationToken cancellationToken = default);

    /// <summary>Modules enabled for the caller's own organization (used by employees/org admins to hide features).</summary>
    Task<IReadOnlyList<OrgModuleFlagDto>> GetMyModulesAsync(CancellationToken cancellationToken = default);
}
