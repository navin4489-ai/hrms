using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Organization;

namespace Wintech.Hrms.Api.Modules.Employees;

/// <summary>Org units, locations and designations — active reference data for filters and pickers.</summary>
public sealed class OrgDataService : IOrgDataService
{
    private readonly AppDbContext _db;

    public OrgDataService(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<OrgUnitDto>> GetOrgUnitsAsync(CancellationToken cancellationToken = default)
    {
        var units = await (
                from u in _db.OrgUnits.AsNoTracking()
                orderby u.Type, u.Name
                select new
                {
                    u.Id, u.Code, u.Name, u.Type, u.ParentId, u.ManagerId, u.Status
                })
            .ToListAsync(cancellationToken);

        var managerNames = await ManagerNamesAsync(units.Where(u => u.ManagerId.HasValue).Select(u => u.ManagerId!.Value), cancellationToken);

        return units.Select(u => new OrgUnitDto
        {
            Id = u.Id,
            Code = u.Code,
            Name = u.Name,
            // Contract vocabulary: LegalEntity is exposed as "Entity".
            Type = u.Type == OrgUnitType.LegalEntity ? "Entity" : u.Type.ToString(),
            ParentId = u.ParentId,
            ManagerName = u.ManagerId.HasValue && managerNames.TryGetValue(u.ManagerId.Value, out var name) ? name : null,
            Status = u.Status.ToString()
        }).ToList();
    }

    public async Task<IReadOnlyList<LocationDto>> GetLocationsAsync(CancellationToken cancellationToken = default) =>
        await _db.Locations.AsNoTracking()
            .OrderBy(l => l.Name)
            .Select(l => new LocationDto
            {
                Id = l.Id,
                Code = l.Code,
                Name = l.Name,
                City = l.City,
                Country = l.Country,
                TimeZone = l.TimeZone,
                Status = l.Status.ToString()
            })
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DesignationDto>> GetDesignationsAsync(CancellationToken cancellationToken = default) =>
        await _db.Designations.AsNoTracking()
            .OrderBy(d => d.Name)
            .Select(d => new DesignationDto
            {
                Id = d.Id,
                Code = d.Code,
                Name = d.Name,
                Grade = d.Grade,
                Status = d.Status.ToString()
            })
            .ToListAsync(cancellationToken);

    private async Task<Dictionary<Guid, string>> ManagerNamesAsync(IEnumerable<Guid> managerIds, CancellationToken cancellationToken)
    {
        var ids = managerIds.Distinct().ToList();
        if (ids.Count == 0) return [];

        return await _db.Employees.AsNoTracking()
            .Where(e => ids.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.DisplayName, cancellationToken);
    }
}
