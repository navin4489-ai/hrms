using System.ComponentModel.DataAnnotations;
using Wintech.Hrms.Api.Domain.Organization;

namespace Wintech.Hrms.Api.Modules.Organizations;

// ---------------------------------------------------------------- read models

public sealed class OrganizationDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Country { get; set; } = "IN";
    public string TimeZone { get; set; } = "Asia/Kolkata";
    public string Currency { get; set; } = "INR";
    public string Status { get; set; } = "Active";
    public DateTime CreatedAt { get; set; }
    public int EmployeeCount { get; set; }
}

public sealed class OrgModuleFlagDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}

public sealed class OrganizationDetailDto
{
    public OrganizationDto Organization { get; set; } = new();
    public IReadOnlyList<OrgModuleFlagDto> Modules { get; set; } = [];
    public string EmployeeCodePrefix { get; set; } = string.Empty;
    public int EmployeeCodePadding { get; set; } = 3;
    public int EmployeeCodeNext { get; set; } = 1;
}

// ---------------------------------------------------------------- write models

public sealed class CreateOrganizationRequest
{
    [Required, StringLength(200, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(50, MinimumLength = 2)]
    [RegularExpression("^[A-Za-z0-9_-]+$", ErrorMessage = "Code may only contain letters, digits, hyphen and underscore.")]
    public string Code { get; set; } = string.Empty;

    [StringLength(2, MinimumLength = 2)]
    public string Country { get; set; } = "IN";

    [StringLength(100)]
    public string TimeZone { get; set; } = "Asia/Kolkata";

    [StringLength(10)]
    public string Currency { get; set; } = "INR";

    /// <summary>Optional employee-code prefix; defaults to "{Code}-EMP".</summary>
    [StringLength(50)]
    public string? EmployeeCodePrefix { get; set; }

    [Range(1, 10)]
    public int EmployeeCodePadding { get; set; } = 3;
}

public sealed class UpdateOrganizationRequest
{
    [Required, StringLength(200, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [StringLength(2, MinimumLength = 2)]
    public string Country { get; set; } = "IN";

    [StringLength(100)]
    public string TimeZone { get; set; } = "Asia/Kolkata";

    [StringLength(10)]
    public string Currency { get; set; } = "INR";

    [Required]
    public ReferenceStatus Status { get; set; } = ReferenceStatus.Active;
}

public sealed class UpdateOrgModulesRequest
{
    [Required]
    public Dictionary<string, bool> Modules { get; set; } = new();
}
