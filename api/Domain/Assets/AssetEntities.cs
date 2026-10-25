namespace Wintech.Hrms.Api.Domain.Assets;

/// <summary>Asset categories (FR-ADM-090, contract section 13).</summary>
public enum AssetType
{
    Laptop = 0,
    Phone = 1,
    Badge = 2,
    Sim = 3,
    Accessory = 4,
    Other = 5
}

public enum AssetStatus
{
    Available = 0,
    Assigned = 1,
    ReturnRequested = 2,
    Returned = 3,
    Retired = 4,
    UnderRepair = 5,
    Lost = 6,
    Damaged = 7
}

/// <summary>
/// IT asset master record with custodian lifecycle (FR-ADM-090..094, FR-EMP-090..093).
/// Employees may acknowledge issue and request returns; only asset.manage completes returns.
/// </summary>
public class Asset
{
    public Guid Id { get; set; }
    public AssetType AssetType { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Unique inventory tag; auto-generated when not supplied by IT (FR-ADM-090).</summary>
    public string AssetTag { get; set; } = string.Empty;
    public string? SerialNumber { get; set; }
    public string? Model { get; set; }
    public DateOnly? PurchaseDate { get; set; }
    public DateOnly? WarrantyExpiry { get; set; }
    public Guid? LocationId { get; set; }

    public AssetStatus Status { get; set; } = AssetStatus.Available;
    public Guid? CustodianEmployeeId { get; set; }
    public DateTime? IssuedAt { get; set; }
    public string? Condition { get; set; }
    public string? Accessories { get; set; }
    /// <summary>Employee handover acknowledgement (FR-EMP-091).</summary>
    public DateTime? AcknowledgedAt { get; set; }
    public string? AcknowledgementNote { get; set; }

    public DateOnly? ReturnDueDate { get; set; }
    public DateTime? ReturnRequestedAt { get; set; }
    public string? ReturnRequestReason { get; set; }
    public string? ReturnCondition { get; set; }
    public DateTime? ReturnedAt { get; set; }
}
