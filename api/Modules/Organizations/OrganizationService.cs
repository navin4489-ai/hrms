using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.Auditing;
using Wintech.Hrms.Api.Common.ErrorHandling;
using Wintech.Hrms.Api.Common.Tenancy;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Organization;

namespace Wintech.Hrms.Api.Modules.Organizations;

public sealed class OrganizationService : IOrganizationService
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;
    private readonly IAuditService _audit;
    private readonly ILogger<OrganizationService> _logger;

    public OrganizationService(AppDbContext db, ITenantContext tenant, IClock clock, IAuditService audit,
        ILogger<OrganizationService> logger)
    {
        _db = db;
        _tenant = tenant;
        _clock = clock;
        _audit = audit;
        _logger = logger;
    }

    public async Task<IReadOnlyList<OrganizationDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var orgs = await _db.Organizations.AsNoTracking()
            .OrderBy(o => o.Name)
            .ToListAsync(cancellationToken);

        var counts = await _db.Employees.AsNoTracking().IgnoreQueryFilters()
            .GroupBy(e => e.OrganizationId)
            .Select(g => new { OrgId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var countByOrg = counts.ToDictionary(c => c.OrgId, c => c.Count);

        return orgs.Select(o => ToDto(o, countByOrg.GetValueOrDefault(o.Id))).ToList();
    }

    public async Task<OrganizationDetailDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var org = await _db.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, cancellationToken)
                  ?? throw new NotFoundException("Organization not found.");
        var count = await _db.Employees.AsNoTracking().IgnoreQueryFilters()
            .CountAsync(e => e.OrganizationId == id, cancellationToken);
        return await BuildDetailAsync(org, count, cancellationToken);
    }

    public async Task<OrganizationDetailDto> CreateAsync(CreateOrganizationRequest request, CancellationToken cancellationToken = default)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        if (await _db.Organizations.AsNoTracking().AnyAsync(o => o.Code.ToUpper() == code, cancellationToken))
            throw new ConflictException($"An organization with code '{code}' already exists.");

        var org = new Organization
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Code = code,
            Country = request.Country.Trim().ToUpperInvariant(),
            TimeZone = request.TimeZone.Trim(),
            Currency = request.Currency.Trim().ToUpperInvariant(),
            Status = ReferenceStatus.Active,
            CreatedAt = _clock.UtcNow
        };
        _db.Organizations.Add(org);

        var prefix = string.IsNullOrWhiteSpace(request.EmployeeCodePrefix)
            ? $"{code}-EMP"
            : request.EmployeeCodePrefix.Trim();
        _db.Set<OrgEmployeeCodeSetting>().Add(new OrgEmployeeCodeSetting
        {
            Id = Guid.NewGuid(),
            OrganizationId = org.Id,
            Prefix = prefix,
            Padding = request.EmployeeCodePadding,
            NextSequence = 1
        });

        foreach (var key in OrgModules.AllKeys)
        {
            _db.Set<OrgModuleSetting>().Add(new OrgModuleSetting
            {
                Id = Guid.NewGuid(),
                OrganizationId = org.Id,
                ModuleKey = key,
                Enabled = OrgModules.DefaultFor(key),
                UpdatedAt = _clock.UtcNow
            });
        }

        await _audit.RecordAsync("organization.create", "Organization", org.Id.ToString(),
            details: new { org.Code, org.Name }, cancellationToken: cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Organization {Code} created.", org.Code);

        return await BuildDetailAsync(org, 0, cancellationToken);
    }

    public async Task<OrganizationDetailDto> UpdateAsync(Guid id, UpdateOrganizationRequest request, CancellationToken cancellationToken = default)
    {
        var org = await _db.Organizations.FirstOrDefaultAsync(o => o.Id == id, cancellationToken)
                  ?? throw new NotFoundException("Organization not found.");

        org.Name = request.Name.Trim();
        org.Country = request.Country.Trim().ToUpperInvariant();
        org.TimeZone = request.TimeZone.Trim();
        org.Currency = request.Currency.Trim().ToUpperInvariant();
        org.Status = request.Status;

        await _audit.RecordAsync("organization.update", "Organization", org.Id.ToString(),
            details: new { org.Code, org.Status }, cancellationToken: cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        var count = await _db.Employees.AsNoTracking().IgnoreQueryFilters()
            .CountAsync(e => e.OrganizationId == id, cancellationToken);
        return await BuildDetailAsync(org, count, cancellationToken);
    }

    public async Task<OrganizationDetailDto> UpdateModulesAsync(Guid id, UpdateOrgModulesRequest request, CancellationToken cancellationToken = default)
    {
        var org = await _db.Organizations.FirstOrDefaultAsync(o => o.Id == id, cancellationToken)
                  ?? throw new NotFoundException("Organization not found.");

        var existing = await _db.Set<OrgModuleSetting>().IgnoreQueryFilters()
            .Where(m => m.OrganizationId == id)
            .ToListAsync(cancellationToken);
        var byKey = existing.ToDictionary(m => m.ModuleKey, StringComparer.OrdinalIgnoreCase);

        foreach (var (key, enabled) in request.Modules)
        {
            if (!OrgModules.IsKnown(key))
                throw new RequestValidationException($"Unknown module '{key}'.");

            if (byKey.TryGetValue(key, out var row))
            {
                row.Enabled = enabled;
                row.UpdatedAt = _clock.UtcNow;
            }
            else
            {
                _db.Set<OrgModuleSetting>().Add(new OrgModuleSetting
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = id,
                    ModuleKey = key,
                    Enabled = enabled,
                    UpdatedAt = _clock.UtcNow
                });
            }
        }

        await _audit.RecordAsync("organization.modules.update", "Organization", org.Id.ToString(),
            details: request.Modules, cancellationToken: cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        var count = await _db.Employees.AsNoTracking().IgnoreQueryFilters()
            .CountAsync(e => e.OrganizationId == id, cancellationToken);
        return await BuildDetailAsync(org, count, cancellationToken);
    }

    public async Task<IReadOnlyList<OrgModuleFlagDto>> GetMyModulesAsync(CancellationToken cancellationToken = default)
    {
        var orgId = _tenant.OrganizationId;
        if (orgId is null) return ResolveFlags(new Dictionary<string, bool>());

        var settings = await _db.Set<OrgModuleSetting>().AsNoTracking().IgnoreQueryFilters()
            .Where(m => m.OrganizationId == orgId.Value)
            .ToListAsync(cancellationToken);
        var map = settings.ToDictionary(m => m.ModuleKey, m => m.Enabled, StringComparer.OrdinalIgnoreCase);
        return ResolveFlags(map);
    }

    private async Task<OrganizationDetailDto> BuildDetailAsync(Organization org, int employeeCount, CancellationToken cancellationToken)
    {
        var settings = await _db.Set<OrgModuleSetting>().AsNoTracking().IgnoreQueryFilters()
            .Where(m => m.OrganizationId == org.Id)
            .ToListAsync(cancellationToken);
        var map = settings.ToDictionary(m => m.ModuleKey, m => m.Enabled, StringComparer.OrdinalIgnoreCase);

        var codeSetting = await _db.Set<OrgEmployeeCodeSetting>().AsNoTracking().IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.OrganizationId == org.Id, cancellationToken);

        return new OrganizationDetailDto
        {
            Organization = ToDto(org, employeeCount),
            Modules = ResolveFlags(map),
            EmployeeCodePrefix = codeSetting?.Prefix ?? $"{org.Code}-EMP",
            EmployeeCodePadding = codeSetting?.Padding ?? 3,
            EmployeeCodeNext = codeSetting?.NextSequence ?? 1
        };
    }

    private static IReadOnlyList<OrgModuleFlagDto> ResolveFlags(IReadOnlyDictionary<string, bool> map) =>
        OrgModules.AllKeys.Select(key => new OrgModuleFlagDto
        {
            Key = key,
            Label = OrgModules.Labels.GetValueOrDefault(key, key),
            Enabled = map.TryGetValue(key, out var v) ? v : OrgModules.DefaultFor(key)
        }).ToList();

    private static OrganizationDto ToDto(Organization o, int employeeCount) => new()
    {
        Id = o.Id,
        Name = o.Name,
        Code = o.Code,
        Country = o.Country,
        TimeZone = o.TimeZone,
        Currency = o.Currency,
        Status = o.Status.ToString(),
        CreatedAt = o.CreatedAt,
        EmployeeCount = employeeCount
    };
}
