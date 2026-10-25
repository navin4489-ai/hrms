using System.ComponentModel.DataAnnotations;
using Wintech.Hrms.Api.Domain.Employees;
using Wintech.Hrms.Api.Domain.Organization;

namespace Wintech.Hrms.Api.Modules.Employees;

/// <summary>Privacy-aware directory row (FR-EMP-024): phone/personal fields are never exposed.</summary>
public sealed class DirectoryItemDto
{
    public Guid EmployeeId { get; init; }
    public string EmployeeNumber { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? Designation { get; init; }
    public string? Department { get; init; }
    public string? Location { get; init; }
    public string WorkEmail { get; init; } = string.Empty;
    public bool HasPhone { get; init; }
    public string? AvatarUrl { get; init; }
}

/// <summary>Admin list row (GET /employees).</summary>
public sealed class EmployeeListItem
{
    public Guid EmployeeId { get; init; }
    public string EmployeeNumber { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string WorkEmail { get; init; } = string.Empty;
    public string? Designation { get; init; }
    public string? Department { get; init; }
    public string? Location { get; init; }
    public EmploymentStatus Status { get; init; }
    public string? ManagerName { get; init; }
    public DateOnly JoiningDate { get; init; }
}

/// <summary>POST /employees (FR-ADM-020/021).</summary>
public sealed class CreateEmployeeRequest
{
    /// <summary>Optional — generated as the next canonical number (BR-001) when omitted.</summary>
    [StringLength(50)]
    public string? EmployeeNumber { get; init; }

    [Required]
    [StringLength(100)]
    public string FirstName { get; init; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string LastName { get; init; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(320)]
    public string WorkEmail { get; init; } = string.Empty;

    [StringLength(50)]
    public string? Phone { get; init; }

    public DateOnly JoinDate { get; init; }

    public Guid DesignationId { get; init; }
    public Guid DepartmentId { get; init; }
    public Guid LocationId { get; init; }
    public Guid? ManagerId { get; init; }

    public EmploymentType EmploymentType { get; init; } = EmploymentType.FullTime;

    /// <summary>Legal-entity org unit the employee belongs to (single-tenant MVP).</summary>
    public Guid LegalEntityId { get; init; }
}

/// <summary>PUT /employees/{id} — partial update; payroll/reporting-relevant changes write employment history (FR-ADM-026).</summary>
public sealed class UpdateEmployeeRequest
{
    [StringLength(100)]
    public string? FirstName { get; init; }

    [StringLength(100)]
    public string? LastName { get; init; }

    [EmailAddress]
    [StringLength(320)]
    public string? WorkEmail { get; init; }

    [StringLength(50)]
    public string? Phone { get; init; }

    public Guid? DesignationId { get; init; }
    public Guid? DepartmentId { get; init; }
    public Guid? LocationId { get; init; }
    public Guid? ManagerId { get; init; }
    public EmploymentType? EmploymentType { get; init; }

    [StringLength(50)]
    public string? CostCentre { get; init; }

    public DateOnly? ProbationEndDate { get; init; }

    /// <summary>Effective date recorded on history rows (defaults to today).</summary>
    public DateOnly? EffectiveDate { get; init; }

    [StringLength(500)]
    public string? Reason { get; init; }
}

/// <summary>POST /employees/{id}/status (FR-ADM-024, BR-008).</summary>
public sealed class ChangeEmployeeStatusRequest
{
    /// <summary>Active | Suspended | Terminated.</summary>
    [Required]
    public EmploymentStatus Status { get; init; }

    public DateOnly EffectiveDate { get; init; }

    [Required]
    [StringLength(500)]
    public string Reason { get; init; } = string.Empty;
}

// ---------------------------------------------------------------- reference data (contract §6)

public sealed class OrgUnitDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    /// <summary>Entity | BusinessUnit | Department | Team.</summary>
    public string Type { get; init; } = string.Empty;
    public Guid? ParentId { get; init; }
    public Guid? ManagerId { get; init; }
    public DateOnly? EffectiveFrom { get; init; }
    public DateOnly? EffectiveTo { get; init; }
    public string? ManagerName { get; init; }
    public string Status { get; init; } = string.Empty;
}

public sealed class LocationDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public string Country { get; init; } = string.Empty;
    public string TimeZone { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
}

public sealed class DesignationDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Grade { get; init; }
    public string Status { get; init; } = string.Empty;
}

/// <summary>Admin reference-data create/update payload. Set status to Inactive instead of deleting used data.</summary>
public sealed class UpsertOrgUnitRequest
{
    [Required, StringLength(50)]
    public string Code { get; init; } = string.Empty;

    [Required, StringLength(200)]
    public string Name { get; init; } = string.Empty;

    /// <summary>Entity (alias for LegalEntity), BusinessUnit, Department or Team.</summary>
    [Required]
    public string Type { get; init; } = string.Empty;

    public Guid? ParentId { get; init; }
    public Guid? ManagerId { get; init; }
    public DateOnly? EffectiveFrom { get; init; }
    public DateOnly? EffectiveTo { get; init; }
    public ReferenceStatus Status { get; init; } = ReferenceStatus.Active;
}

public sealed class UpsertLocationRequest
{
    [Required, StringLength(50)]
    public string Code { get; init; } = string.Empty;

    [Required, StringLength(200)]
    public string Name { get; init; } = string.Empty;

    [Required, StringLength(100)]
    public string City { get; init; } = string.Empty;

    [StringLength(2)]
    public string Country { get; init; } = "IN";

    [StringLength(100)]
    public string TimeZone { get; init; } = "Asia/Kolkata";

    public ReferenceStatus Status { get; init; } = ReferenceStatus.Active;
}

public sealed class UpsertDesignationRequest
{
    [Required, StringLength(50)]
    public string Code { get; init; } = string.Empty;

    [Required, StringLength(200)]
    public string Name { get; init; } = string.Empty;

    [StringLength(50)]
    public string? Grade { get; init; }

    public ReferenceStatus Status { get; init; } = ReferenceStatus.Active;
}
