using Wintech.Hrms.Api.Common.Registration;

namespace Wintech.Hrms.Api.Modules.Employees;

/// <summary>Reference data for filters and drop-downs (contract §6: /org-units, /locations, /designations).</summary>
public interface IOrgDataService : IAppService
{
    Task<IReadOnlyList<OrgUnitDto>> GetOrgUnitsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LocationDto>> GetLocationsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DesignationDto>> GetDesignationsAsync(CancellationToken cancellationToken = default);

    Task<OrgUnitDto> CreateOrgUnitAsync(UpsertOrgUnitRequest request, CancellationToken cancellationToken = default);

    Task<OrgUnitDto> UpdateOrgUnitAsync(Guid id, UpsertOrgUnitRequest request, CancellationToken cancellationToken = default);

    Task<LocationDto> CreateLocationAsync(UpsertLocationRequest request, CancellationToken cancellationToken = default);

    Task<LocationDto> UpdateLocationAsync(Guid id, UpsertLocationRequest request, CancellationToken cancellationToken = default);

    Task<DesignationDto> CreateDesignationAsync(UpsertDesignationRequest request, CancellationToken cancellationToken = default);

    Task<DesignationDto> UpdateDesignationAsync(Guid id, UpsertDesignationRequest request, CancellationToken cancellationToken = default);
}
