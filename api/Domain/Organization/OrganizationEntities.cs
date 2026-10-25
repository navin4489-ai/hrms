using Wintech.Hrms.Api.Common.Tenancy;

namespace Wintech.Hrms.Api.Domain.Organization;

public enum OrgUnitType
{
    LegalEntity = 0,
    BusinessUnit = 1,
    Department = 2,
    Team = 3
}

public enum ReferenceStatus
{
    Active = 0,
    Inactive = 1
}

/// <summary>Tenant root (single-tenant for MVP; multi-entity support via <see cref="OrgUnit"/> of type LegalEntity).</summary>
public class Organization
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Country { get; set; } = "IN";
    public string TimeZone { get; set; } = "Asia/Kolkata";
    public string Currency { get; set; } = "INR";
    public ReferenceStatus Status { get; set; } = ReferenceStatus.Active;
    public DateTime CreatedAt { get; set; }
}

/// <summary>Legal entities, business units, departments and teams — effective-dated reference data (FR-ADM-010).</summary>
public class OrgUnit : IOrgOwned
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public OrgUnitType Type { get; set; }
    public Guid? ParentId { get; set; }
    public OrgUnit? Parent { get; set; }
    public Guid? ManagerId { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public ReferenceStatus Status { get; set; } = ReferenceStatus.Active;
    public ICollection<OrgUnit> Children { get; set; } = [];
}

public class Location : IOrgOwned
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Country { get; set; } = "IN";
    public string TimeZone { get; set; } = "Asia/Kolkata";
    public ReferenceStatus Status { get; set; } = ReferenceStatus.Active;
}

public class Designation : IOrgOwned
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Grade { get; set; }
    public ReferenceStatus Status { get; set; } = ReferenceStatus.Active;
}

/// <summary>
/// Per-organization module enablement (FR-ADM-002). SuperAdmin turns features on/off per org;
/// org admins and employees only see enabled modules. A missing row means "use the module default".
/// </summary>
public class OrgModuleSetting : IOrgOwned
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }

    /// <summary>Module key from <see cref="Wintech.Hrms.Api.Common.Tenancy.OrgModules"/>.</summary>
    public string ModuleKey { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public DateTime UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}

/// <summary>
/// Per-organization employee-code generation settings (FR-ADM-001). Codes are prefixed with the
/// org code, e.g. <c>WINTECH-EMP001</c>, and sequenced independently per organization.
/// </summary>
public class OrgEmployeeCodeSetting : IOrgOwned
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }

    /// <summary>Prefix before the sequence; defaults to "{OrgCode}-EMP".</summary>
    public string Prefix { get; set; } = string.Empty;
    public int Padding { get; set; } = 3;
    public int NextSequence { get; set; } = 1;
}
