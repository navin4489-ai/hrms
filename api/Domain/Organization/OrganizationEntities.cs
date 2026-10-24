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
public class OrgUnit
{
    public Guid Id { get; set; }
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

public class Location
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Country { get; set; } = "IN";
    public string TimeZone { get; set; } = "Asia/Kolkata";
    public ReferenceStatus Status { get; set; } = ReferenceStatus.Active;
}

public class Designation
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Grade { get; set; }
    public ReferenceStatus Status { get; set; } = ReferenceStatus.Active;
}
