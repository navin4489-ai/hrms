using Wintech.Hrms.Api.Common.Registration;

namespace Wintech.Hrms.Api.Modules.Employees;

/// <summary>Reference data for filters and drop-downs (contract §6: /org-units, /locations, /designations).</summary>
public interface IOrgDataService : IAppService
{
    Task<IReadOnlyList<OrgUnitDto>> GetOrgUnitsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LocationDto>> GetLocationsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DesignationDto>> GetDesignationsAsync(CancellationToken cancellationToken = default);
}
