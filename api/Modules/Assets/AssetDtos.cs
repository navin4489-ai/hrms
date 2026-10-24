using System.ComponentModel.DataAnnotations;
using Wintech.Hrms.Api.Domain.Assets;

namespace Wintech.Hrms.Api.Modules.Assets;

// ------------------------------------------------------------------ request forms

public sealed class AcknowledgeAssetRequest
{
    [Required, StringLength(100)]
    public string Condition { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Note { get; set; }
}

public sealed class ReturnAssetRequest
{
    [Required, StringLength(500)]
    public string Reason { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string Condition { get; set; } = string.Empty;
}

public sealed class CreateAssetForm
{
    [Required]
    public AssetType AssetType { get; set; }

    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(50)]
    public string? AssetTag { get; set; }

    [StringLength(100)]
    public string? SerialNumber { get; set; }

    [StringLength(100)]
    public string? Model { get; set; }

    public DateOnly? PurchaseDate { get; set; }

    public DateOnly? WarrantyExpiry { get; set; }

    public Guid? LocationId { get; set; }
}

public sealed class AssignAssetForm
{
    [Required]
    public Guid EmployeeId { get; set; }

    [Required, StringLength(100)]
    public string Condition { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Accessories { get; set; }

    public DateOnly? ReturnDueDate { get; set; }
}

public sealed class AssetInventoryQuery
{
    public AssetStatus? Status { get; set; }
    public AssetType? AssetType { get; set; }
    /// <summary>Search name, tag or serial number.</summary>
    public string? Q { get; set; }
}

// ------------------------------------------------------------------ responses

/// <summary>Asset as seen by its custodian (contract section 13, GET /assets).</summary>
public sealed class AssetMineItem
{
    public Guid Id { get; init; }
    public AssetType AssetType { get; init; }
    public string Name { get; init; } = string.Empty;
    public string AssetTag { get; init; } = string.Empty;
    public string? SerialNumber { get; init; }
    public string? Model { get; init; }
    public AssetStatus Status { get; init; }
    public DateTime? IssuedAt { get; init; }
    public string? Condition { get; init; }
    public string? Accessories { get; init; }
    public DateTime? AcknowledgedAt { get; init; }
    public DateOnly? ReturnDueDate { get; init; }
}

/// <summary>Inventory row (contract section 13, GET /assets/inventory).</summary>
public sealed class AssetInventoryItem
{
    public Guid Id { get; init; }
    public AssetType AssetType { get; init; }
    public string Name { get; init; } = string.Empty;
    public string AssetTag { get; init; } = string.Empty;
    public string? SerialNumber { get; init; }
    public AssetStatus Status { get; init; }
    public DateOnly? WarrantyExpiry { get; init; }
    public string? CustodianEmployeeName { get; init; }
    public string? LocationName { get; init; }
}

/// <summary>Paged inventory plus the overdue-return counter (FR-ADM-093).</summary>
public sealed class AssetInventoryResult
{
    public IReadOnlyList<AssetInventoryItem> Items { get; init; } = [];
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int Total { get; init; }
    public bool HasNextPage => Page * PageSize < Total;
    /// <summary>Assigned/return-requested assets past their return due date.</summary>
    public int OverdueReturns { get; init; }
}

public sealed class AssetAssignmentResult
{
    public Guid AssetId { get; init; }
    public string AssetTag { get; init; } = string.Empty;
    public Guid EmployeeId { get; init; }
    public DateTime IssuedAt { get; init; }
    public string Condition { get; init; } = string.Empty;
    public string? Accessories { get; init; }
    public DateOnly? ReturnDueDate { get; init; }
    public AssetStatus Status { get; init; }
}

public sealed class AssetReturnResult
{
    public AssetStatus Status { get; init; }
}
