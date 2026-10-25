using Wintech.Hrms.Api.Common.Tenancy;
using Wintech.Hrms.Api.Domain.Organization;

namespace Wintech.Hrms.Api.Domain.Employees;

public enum EmploymentStatus
{
    Active = 0,
    Probation = 1,
    OnNotice = 2,
    Suspended = 3,
    Terminated = 4
}

public enum EmploymentType
{
    FullTime = 0,
    PartTime = 1,
    Contract = 2,
    Intern = 3
}

public enum Gender
{
    PreferNotToSay = 0,
    Female = 1,
    Male = 2,
    Other = 3
}

public enum HistoryEventType
{
    Joining = 0,
    ProbationEnd = 1,
    Confirmation = 2,
    Promotion = 3,
    DesignationChange = 4,
    DepartmentChange = 5,
    ManagerChange = 6,
    LocationChange = 7,
    Transfer = 8,
    EmploymentTypeChange = 9,
    StatusChange = 10,
    Termination = 11
}

public enum ProfileChangeStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Cancelled = 3
}

/// <summary>Employee master record (PII). One canonical employee number per BR-001.</summary>
public class Employee : IOrgOwned
{
    public Guid Id { get; set; }
    public string EmployeeNumber { get; set; } = string.Empty;
    public Guid OrganizationId { get; set; }
    public Organization.Organization? Organization { get; set; }

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    /// <summary>Denormalized "FirstName LastName"; maintained automatically by the DbContext on save.</summary>
    public string DisplayName { get; set; } = string.Empty;

    public string WorkEmail { get; set; } = string.Empty;
    public string? Phone { get; set; }

    public DateOnly JoinDate { get; set; }
    public DateOnly? ProbationEndDate { get; set; }
    public DateOnly? ConfirmationDate { get; set; }
    public EmploymentType EmploymentType { get; set; } = EmploymentType.FullTime;
    public EmploymentStatus EmploymentStatus { get; set; } = EmploymentStatus.Active;
    public DateOnly? StatusEffectiveDate { get; set; }
    public DateOnly? ResignationDate { get; set; }
    public DateOnly? LastWorkingDate { get; set; }

    public Guid DesignationId { get; set; }
    public Designation? Designation { get; set; }
    public Guid DepartmentId { get; set; }
    public OrgUnit? Department { get; set; }
    public Guid LocationId { get; set; }
    public Location? Location { get; set; }
    public Guid? ManagerId { get; set; }
    public Employee? Manager { get; set; }
    public string? CostCentre { get; set; }
    public string? PhotoStorageKey { get; set; }

    public EmployeePersonalDetail? PersonalDetail { get; set; }
    public EmployeeSensitiveData? SensitiveData { get; set; }
    public ICollection<EmploymentHistory> EmploymentHistory { get; set; } = [];
    public ICollection<ProfileChangeRequest> ProfileChangeRequests { get; set; } = [];

    public bool IsActive => EmploymentStatus is EmploymentStatus.Active or EmploymentStatus.Probation;
}

/// <summary>Personal/contact details, stored separately from work data (BRD section 9).</summary>
public class EmployeePersonalDetail
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public string? PreferredName { get; set; }
    public string? PersonalEmail { get; set; }
    public string? PersonalPhone { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public Gender Gender { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? PostalCode { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? MaritalStatus { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Highly restricted fields (BRD 11.1): bank and national identifiers. Access requires a
/// dedicated permission, values are masked by default and every read/export is audited (BR-009).
/// </summary>
public class EmployeeSensitiveData
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public string? BankAccountNumber { get; set; }
    public string? BankName { get; set; }
    public string? BankBranch { get; set; }
    public string? PaymentMethod { get; set; }
    public string? NationalIdType { get; set; }
    public string? NationalIdNumber { get; set; }
    public string? TaxId { get; set; }
    public DateTime? BankVerifiedAt { get; set; }
    public Guid? BankVerifiedByUserId { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Append-only history of effective-dated employment changes (FR-ADM-026, BR-002).</summary>
public class EmploymentHistory
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public HistoryEventType EventType { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? Reason { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Employee-submitted change to a sensitive field (FR-EMP-022) routed through approvals.</summary>
public class ProfileChangeRequest
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public string Field { get; set; } = string.Empty;
    /// <summary>Masked in every response until verified by HR.</summary>
    public string RequestedValue { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public ProfileChangeStatus Status { get; set; } = ProfileChangeStatus.Pending;
    public Guid? ApprovalTaskId { get; set; }
    public DateTime RequestedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionComment { get; set; }
}
