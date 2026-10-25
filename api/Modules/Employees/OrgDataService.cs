using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.Auditing;
using Wintech.Hrms.Api.Common.ErrorHandling;
using Wintech.Hrms.Api.Common.Tenancy;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Organization;

namespace Wintech.Hrms.Api.Modules.Employees;

/// <summary>Org units, locations and designations — active reference data for filters and pickers.</summary>
public sealed class OrgDataService : IOrgDataService
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditService _audit;

    public OrgDataService(AppDbContext db, ITenantContext tenant, IAuditService audit)
    {
        _db = db;
        _tenant = tenant;
        _audit = audit;
    }

    /// <summary>The org a duplicate check / new row must be scoped to (SuperAdmins target via header).</summary>
    private Guid ScopeOrgId => _tenant.RequireOrganizationId();

    public async Task<IReadOnlyList<OrgUnitDto>> GetOrgUnitsAsync(CancellationToken cancellationToken = default)
    {
        var units = await (
                from u in _db.OrgUnits.AsNoTracking()
                orderby u.Type, u.Name
                select new
                {
                     u.Id, u.Code, u.Name, u.Type, u.ParentId, u.ManagerId,
                     u.EffectiveFrom, u.EffectiveTo, u.Status
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
            ManagerId = u.ManagerId,
            EffectiveFrom = u.EffectiveFrom,
            EffectiveTo = u.EffectiveTo,
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

    public async Task<OrgUnitDto> CreateOrgUnitAsync(UpsertOrgUnitRequest request,
        CancellationToken cancellationToken = default)
    {
        var type = ParseOrgUnitType(request.Type);
        ValidateDateRange(request.EffectiveFrom, request.EffectiveTo);
        var orgId = ScopeOrgId;
        await ValidateOrgUnitReferencesAsync(
            request.ParentId, request.ManagerId, null, orgId, cancellationToken);
        var code = request.Code.Trim();
        if (await _db.OrgUnits.AnyAsync(unit => unit.OrganizationId == orgId && unit.Code.ToLower() == code.ToLower(), cancellationToken))
            throw new ConflictException($"An organization unit with code '{code}' already exists.");

        var unit = new OrgUnit
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            Code = code,
            Name = request.Name.Trim(),
            Type = type,
            ParentId = request.ParentId,
            ManagerId = request.ManagerId,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo,
            Status = request.Status
        };
        _db.OrgUnits.Add(unit);
        _audit.Add("org_unit.create", nameof(OrgUnit), unit.Id.ToString(), details: new
        {
            unit.Code, unit.Name, Type = PublicOrgUnitType(unit.Type)
        });
        await _db.SaveChangesAsync(cancellationToken);
        return await MapOrgUnitAsync(unit, cancellationToken);
    }

    public async Task<OrgUnitDto> UpdateOrgUnitAsync(Guid id, UpsertOrgUnitRequest request,
        CancellationToken cancellationToken = default)
    {
        var unit = await _db.OrgUnits.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException("Organization unit not found.");
        var type = ParseOrgUnitType(request.Type);
        ValidateDateRange(request.EffectiveFrom, request.EffectiveTo);
        await ValidateOrgUnitReferencesAsync(
            request.ParentId, request.ManagerId, id, unit.OrganizationId, cancellationToken);
        if (await _db.OrgUnits.AnyAsync(item => item.Id != id &&
                item.OrganizationId == unit.OrganizationId &&
                item.Code.ToLower() == request.Code.Trim().ToLower(), cancellationToken))
            throw new ConflictException($"An organization unit with code '{request.Code.Trim()}' already exists.");

        var old = new { unit.Code, unit.Name, Type = PublicOrgUnitType(unit.Type), unit.ParentId, unit.ManagerId, unit.Status };
        unit.Code = request.Code.Trim();
        unit.Name = request.Name.Trim();
        unit.Type = type;
        unit.ParentId = request.ParentId;
        unit.ManagerId = request.ManagerId;
        unit.EffectiveFrom = request.EffectiveFrom;
        unit.EffectiveTo = request.EffectiveTo;
        unit.Status = request.Status;
        _audit.Add("org_unit.update", nameof(OrgUnit), id.ToString(), details: new
        {
            old,
            current = new { unit.Code, unit.Name, Type = PublicOrgUnitType(unit.Type), unit.ParentId, unit.ManagerId, unit.Status }
        });
        await _db.SaveChangesAsync(cancellationToken);
        return await MapOrgUnitAsync(unit, cancellationToken);
    }

    public async Task<LocationDto> CreateLocationAsync(UpsertLocationRequest request,
        CancellationToken cancellationToken = default)
    {
        var code = request.Code.Trim();
        var orgId = ScopeOrgId;
        if (await _db.Locations.AnyAsync(item => item.OrganizationId == orgId && item.Code.ToLower() == code.ToLower(), cancellationToken))
            throw new ConflictException($"A location with code '{code}' already exists.");
        var location = new Location
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            Code = code,
            Name = request.Name.Trim(),
            City = request.City.Trim(),
            Country = request.Country.Trim().ToUpperInvariant(),
            TimeZone = request.TimeZone.Trim(),
            Status = request.Status
        };
        _db.Locations.Add(location);
        _audit.Add("location.create", nameof(Location), location.Id.ToString(), details: new
        {
            location.Code, location.Name, location.City, location.Status
        });
        await _db.SaveChangesAsync(cancellationToken);
        return MapLocation(location);
    }

    public async Task<LocationDto> UpdateLocationAsync(Guid id, UpsertLocationRequest request,
        CancellationToken cancellationToken = default)
    {
        var location = await _db.Locations.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException("Location not found.");
        var code = request.Code.Trim();
        if (await _db.Locations.AnyAsync(item => item.Id != id && item.OrganizationId == location.OrganizationId && item.Code.ToLower() == code.ToLower(), cancellationToken))
            throw new ConflictException($"A location with code '{code}' already exists.");
        var old = new { location.Code, location.Name, location.City, location.Country, location.TimeZone, location.Status };
        location.Code = code;
        location.Name = request.Name.Trim();
        location.City = request.City.Trim();
        location.Country = request.Country.Trim().ToUpperInvariant();
        location.TimeZone = request.TimeZone.Trim();
        location.Status = request.Status;
        _audit.Add("location.update", nameof(Location), id.ToString(), details: new
        {
            old,
            current = new { location.Code, location.Name, location.City, location.Country, location.TimeZone, location.Status }
        });
        await _db.SaveChangesAsync(cancellationToken);
        return MapLocation(location);
    }

    public async Task<DesignationDto> CreateDesignationAsync(UpsertDesignationRequest request,
        CancellationToken cancellationToken = default)
    {
        var code = request.Code.Trim();
        var orgId = ScopeOrgId;
        if (await _db.Designations.AnyAsync(item => item.OrganizationId == orgId && item.Code.ToLower() == code.ToLower(), cancellationToken))
            throw new ConflictException($"A designation with code '{code}' already exists.");
        var designation = new Designation
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            Code = code,
            Name = request.Name.Trim(),
            Grade = string.IsNullOrWhiteSpace(request.Grade) ? null : request.Grade.Trim(),
            Status = request.Status
        };
        _db.Designations.Add(designation);
        _audit.Add("designation.create", nameof(Designation), designation.Id.ToString(), details: new
        {
            designation.Code, designation.Name, designation.Grade, designation.Status
        });
        await _db.SaveChangesAsync(cancellationToken);
        return MapDesignation(designation);
    }

    public async Task<DesignationDto> UpdateDesignationAsync(Guid id, UpsertDesignationRequest request,
        CancellationToken cancellationToken = default)
    {
        var designation = await _db.Designations.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException("Designation not found.");
        var code = request.Code.Trim();
        if (await _db.Designations.AnyAsync(item => item.Id != id && item.OrganizationId == designation.OrganizationId && item.Code.ToLower() == code.ToLower(), cancellationToken))
            throw new ConflictException($"A designation with code '{code}' already exists.");
        var old = new { designation.Code, designation.Name, designation.Grade, designation.Status };
        designation.Code = code;
        designation.Name = request.Name.Trim();
        designation.Grade = string.IsNullOrWhiteSpace(request.Grade) ? null : request.Grade.Trim();
        designation.Status = request.Status;
        _audit.Add("designation.update", nameof(Designation), id.ToString(), details: new
        {
            old,
            current = new { designation.Code, designation.Name, designation.Grade, designation.Status }
        });
        await _db.SaveChangesAsync(cancellationToken);
        return MapDesignation(designation);
    }

    private async Task ValidateOrgUnitReferencesAsync(Guid? parentId, Guid? managerId, Guid? unitId,
        Guid organizationId, CancellationToken cancellationToken)
    {
        if (parentId.HasValue)
        {
            if (parentId == unitId)
                throw new RequestValidationException("An organization unit cannot be its own parent.",
                    new() { ["parentId"] = ["Choose another parent organization unit."] });
            var parent = await _db.OrgUnits.AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == parentId.Value &&
                    item.OrganizationId == organizationId, cancellationToken);
            if (parent is null)
                throw new RequestValidationException("The parent organization unit is invalid.",
                    new() { ["parentId"] = ["The parent organization unit does not exist."] });
            if (unitId is { } currentId)
            {
                var visited = new HashSet<Guid>();
                var cursor = parent;
                while (cursor is not null && visited.Add(cursor.Id))
                {
                    if (cursor.Id == currentId)
                        throw new RequestValidationException("Organization hierarchy would contain a cycle.",
                            new() { ["parentId"] = ["Choose a parent outside this unit's hierarchy."] });
                    cursor = cursor.ParentId is { } next
                        ? await _db.OrgUnits.AsNoTracking().FirstOrDefaultAsync(item => item.Id == next, cancellationToken)
                        : null;
                }
            }
        }
        if (managerId.HasValue && !await _db.Employees.AsNoTracking()
                .AnyAsync(item => item.Id == managerId.Value &&
                    item.OrganizationId == organizationId, cancellationToken))
            throw new RequestValidationException("The reporting manager is invalid.",
                new() { ["managerId"] = ["The selected manager does not exist."] });
    }

    private static void ValidateDateRange(DateOnly? from, DateOnly? to)
    {
        if (from.HasValue && to.HasValue && to.Value < from.Value)
            throw new RequestValidationException("The effective date range is invalid.",
                new() { ["effectiveTo"] = ["End date must be on or after the start date."] });
    }

    private static OrgUnitType ParseOrgUnitType(string value)
    {
        if (string.Equals(value.Trim(), "Entity", StringComparison.OrdinalIgnoreCase))
            return OrgUnitType.LegalEntity;
        if (Enum.TryParse<OrgUnitType>(value.Trim(), true, out var type) && Enum.IsDefined(type))
            return type;
        throw new RequestValidationException("Organization unit type is invalid.",
            new() { ["type"] = ["Choose Entity, BusinessUnit, Department or Team."] });
    }

    private async Task<OrgUnitDto> MapOrgUnitAsync(OrgUnit unit, CancellationToken cancellationToken)
    {
        var managerName = unit.ManagerId is { } id
            ? await _db.Employees.AsNoTracking().Where(employee => employee.Id == id)
                .Select(employee => employee.DisplayName).FirstOrDefaultAsync(cancellationToken)
            : null;
        return new OrgUnitDto
        {
            Id = unit.Id,
            Code = unit.Code,
            Name = unit.Name,
            Type = PublicOrgUnitType(unit.Type),
            ParentId = unit.ParentId,
            ManagerId = unit.ManagerId,
            EffectiveFrom = unit.EffectiveFrom,
            EffectiveTo = unit.EffectiveTo,
            ManagerName = managerName,
            Status = unit.Status.ToString()
        };
    }

    private static string PublicOrgUnitType(OrgUnitType type) =>
        type == OrgUnitType.LegalEntity ? "Entity" : type.ToString();

    private static LocationDto MapLocation(Location location) => new()
    {
        Id = location.Id,
        Code = location.Code,
        Name = location.Name,
        City = location.City,
        Country = location.Country,
        TimeZone = location.TimeZone,
        Status = location.Status.ToString()
    };

    private static DesignationDto MapDesignation(Designation designation) => new()
    {
        Id = designation.Id,
        Code = designation.Code,
        Name = designation.Name,
        Grade = designation.Grade,
        Status = designation.Status.ToString()
    };

    private async Task<Dictionary<Guid, string>> ManagerNamesAsync(IEnumerable<Guid> managerIds, CancellationToken cancellationToken)
    {
        var ids = managerIds.Distinct().ToList();
        if (ids.Count == 0) return [];

        return await _db.Employees.AsNoTracking()
            .Where(e => ids.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.DisplayName, cancellationToken);
    }
}
