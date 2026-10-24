using System.ComponentModel.DataAnnotations;
using Wintech.Hrms.Api.Domain.Employees;

namespace Wintech.Hrms.Api.Modules.Profile;

/// <summary>Personal + work profile returned by GET /me and GET /employees/{id} (contract §5/§6).</summary>
public sealed record EmployeeProfileDto
{
    public Guid EmployeeId { get; init; }
    public string EmployeeNumber { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? PreferredName { get; init; }
    public string WorkEmail { get; init; } = string.Empty;
    public string? PersonalEmail { get; init; }
    public string? Phone { get; init; }
    public DateOnly? DateOfBirth { get; init; }
    public Gender Gender { get; init; }
    public string? Address { get; init; }
    public string? EmergencyContactName { get; init; }
    public string? EmergencyContactPhone { get; init; }
    public string? Designation { get; init; }
    public string? Department { get; init; }
    public Guid DepartmentId { get; init; }
    public string? Location { get; init; }
    public Guid LocationId { get; init; }
    public string? ManagerName { get; init; }
    public Guid? ManagerEmployeeId { get; init; }
    public DateOnly JoiningDate { get; init; }
    public EmploymentType EmploymentType { get; init; }
    public EmploymentStatus EmploymentStatus { get; init; }
    /// <summary>Admin-shaped alias of <see cref="EmploymentStatus"/>; only populated on /employees/{id}.</summary>
    public EmploymentStatus? Status { get; init; }
    public DateOnly? ProbationEndDate { get; init; }
    public string? LegalEntity { get; init; }
    public string? CostCentre { get; init; }
    public string? AvatarUrl { get; init; }

    // ---- sensitive fields (masked unless document.sensitive + ?unmask=true, then audited) ----
    public string? BankAccountNumber { get; init; }
    public string? BankName { get; init; }
    public string? NationalIdType { get; init; }
    public string? NationalIdNumber { get; init; }
    public string? TaxId { get; init; }
}

/// <summary>PUT /me/profile — non-sensitive self-service fields only (FR-EMP-021).</summary>
public sealed class UpdateProfileRequest
{
    [StringLength(100)]
    public string? PreferredName { get; init; }

    [StringLength(50)]
    public string? Phone { get; init; }

    [EmailAddress]
    [StringLength(320)]
    public string? PersonalEmail { get; init; }

    [StringLength(500)]
    public string? Address { get; init; }

    [StringLength(200)]
    public string? EmergencyContactName { get; init; }

    [StringLength(50)]
    public string? EmergencyContactPhone { get; init; }
}

public sealed record ProfileSavedResponse(string Status, string Message);

/// <summary>POST /me/sensitive-change-requests — bank details / legal name / national ID (FR-EMP-022).</summary>
public sealed class SensitiveChangeRequest
{
    /// <summary>One of the allowed sensitive fields (BankAccount, BankName, LegalName, NationalId, ...).</summary>
    [Required]
    [StringLength(100)]
    public string Field { get; init; } = string.Empty;

    [Required]
    [StringLength(1000)]
    public string RequestedValue { get; init; } = string.Empty;

    [StringLength(1000)]
    public string? Reason { get; init; }
}

/// <summary>202 response — the requested value stays masked everywhere until HR verifies it.</summary>
public sealed record SensitiveChangeResponse(Guid Id, string Status);

public sealed record AvatarResponse(string AvatarUrl);

/// <summary>Employment history row (FR-ADM-026 / FR-EMP-025).</summary>
public sealed class EmploymentHistoryItemDto
{
    public HistoryEventType EventType { get; init; }
    public DateOnly EffectiveDate { get; init; }
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
    public string? Reason { get; init; }
    public string? ApprovedBy { get; init; }
}

/// <summary>GET /me/notification-preferences item (FR-EMP-121).</summary>
public sealed class NotificationPreferenceDto
{
    public string Category { get; init; } = string.Empty;
    public bool InApp { get; init; }
    public bool Push { get; init; }
    public bool Email { get; init; }
    public bool Mandatory { get; init; }
}

/// <summary>PUT /me/notification-preferences item — null channel values mean "keep current".</summary>
public sealed class NotificationPreferenceItem
{
    [Required]
    [StringLength(100)]
    public string? Category { get; init; }

    public bool? InApp { get; init; }
    public bool? Push { get; init; }
    public bool? Email { get; init; }
}

public sealed class UpdateNotificationPreferencesRequest
{
    [Required]
    [MinLength(1)]
    public List<NotificationPreferenceItem> Items { get; init; } = [];
}
