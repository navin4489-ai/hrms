using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.Auditing;
using Wintech.Hrms.Api.Common.CurrentUser;
using Wintech.Hrms.Api.Common.ErrorHandling;
using Wintech.Hrms.Api.Common.Notifications;
using Wintech.Hrms.Api.Common.Paging;
using Wintech.Hrms.Api.Common.Registration;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Assets;

namespace Wintech.Hrms.Api.Modules.Assets;

/// <summary>
/// IT asset lifecycle (FR-EMP-090..093, FR-ADM-090..094).
/// Employees see and acknowledge their own assets and can only *request* a return (FR-EMP-093);
/// inventory administration (create/assign/complete returns) requires asset.manage.
/// </summary>
public interface IAssetService : IAppService
{
    Task<IReadOnlyList<AssetMineItem>> ListMineAsync(CancellationToken cancellationToken = default);
    Task<AssetMineItem> AcknowledgeAsync(Guid id, string condition, string? note, CancellationToken cancellationToken = default);
    Task<AssetReturnResult> RequestReturnAsync(Guid id, string reason, string condition, CancellationToken cancellationToken = default);
    Task<AssetInventoryResult> ListInventoryAsync(AssetInventoryQuery query, PageRequest page, CancellationToken cancellationToken = default);
    Task<AssetInventoryItem> CreateAssetAsync(CreateAssetForm form, CancellationToken cancellationToken = default);
    Task<AssetAssignmentResult> AssignAsync(Guid id, AssignAssetForm form, CancellationToken cancellationToken = default);
    Task<AssetLifecycleResult> ApplyLifecycleActionAsync(Guid id, AssetLifecycleRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class AssetService : IAssetService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;
    private readonly INotificationPublisher _notifications;
    private readonly IClock _clock;

    public AssetService(AppDbContext db, ICurrentUser currentUser, IAuditService audit,
        INotificationPublisher notifications, IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _notifications = notifications;
        _clock = clock;
    }

    // ---------------------------------------------------------------- employee self-service

    public async Task<IReadOnlyList<AssetMineItem>> ListMineAsync(CancellationToken cancellationToken = default)
    {
        _currentUser.EnsureActive();
        var employeeId = RequireEmployeeId();

        var assets = await _db.Set<Asset>().AsNoTracking()
            .Where(a => a.CustodianEmployeeId == employeeId &&
                        a.Status != AssetStatus.Available &&
                        a.Status != AssetStatus.Retired)
            .OrderBy(a => a.AssetType).ThenBy(a => a.Name)
            .ToListAsync(cancellationToken);
        return assets.Select(MapMine).ToList();
    }

    public async Task<AssetMineItem> AcknowledgeAsync(Guid id, string condition, string? note,
        CancellationToken cancellationToken = default)
    {
        _currentUser.EnsureActive();
        var employeeId = RequireEmployeeId();
        var asset = await MineAsync(id, employeeId, cancellationToken);

        if (asset.AcknowledgedAt is not null)
            throw new ConflictException("This asset has already been acknowledged.");

        asset.AcknowledgedAt = _clock.UtcNow;
        asset.Condition = condition.Trim();
        asset.AcknowledgementNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        _audit.Add("Asset.Acknowledged", nameof(Asset), asset.Id.ToString(),
            details: new { asset.AssetTag, condition });
        await _db.SaveChangesAsync(cancellationToken);

        return MapMine(asset);
    }

    public async Task<AssetReturnResult> RequestReturnAsync(Guid id, string reason, string condition,
        CancellationToken cancellationToken = default)
    {
        _currentUser.EnsureActive();
        var employeeId = RequireEmployeeId();
        var asset = await MineAsync(id, employeeId, cancellationToken);

        // FR-EMP-093: employees raise a return request only — authorized verification (asset.manage)
        // moves the asset to Returned.
        if (asset.Status == AssetStatus.ReturnRequested)
            throw new ConflictException("A return has already been requested for this asset.");

        asset.Status = AssetStatus.ReturnRequested;
        asset.ReturnRequestedAt = _clock.UtcNow;
        asset.ReturnRequestReason = reason.Trim();
        asset.ReturnCondition = condition.Trim();

        _audit.Add("Asset.ReturnRequested", nameof(Asset), asset.Id.ToString(),
            details: new { asset.AssetTag, reason, condition });

        await NotifyAssetAdminsAsync($"Asset return requested — {asset.Name}",
            $"{_currentUser.DisplayName} requested the return of {asset.AssetTag} ({asset.Name}).");

        await _db.SaveChangesAsync(cancellationToken);
        return new AssetReturnResult { Status = asset.Status };
    }

    // ---------------------------------------------------------------- inventory administration

    public async Task<AssetInventoryResult> ListInventoryAsync(AssetInventoryQuery query, PageRequest page,
        CancellationToken cancellationToken = default)
    {
        _currentUser.EnsureActive();

        IQueryable<Asset> assets = _db.Set<Asset>().AsNoTracking();

        if (query.Status is { } status)
            assets = assets.Where(a => a.Status == status);
        if (query.AssetType is { } type)
            assets = assets.Where(a => a.AssetType == type);
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim();
            assets = assets.Where(a => a.Name.Contains(term) || a.AssetTag.Contains(term) ||
                                       (a.SerialNumber != null && a.SerialNumber.Contains(term)));
        }

        var today = _clock.Today;
        var overdueReturns = await _db.Set<Asset>().AsNoTracking()
            .CountAsync(a => (a.Status == AssetStatus.Assigned || a.Status == AssetStatus.ReturnRequested) &&
                             a.ReturnDueDate != null && a.ReturnDueDate < today, cancellationToken);

        var paged = await assets.OrderBy(a => a.AssetTag)
            .ToPagedAsync(page, MapInventoryItem, cancellationToken);

        // Enrich custodian/location names in one round-trip.
        var rows = await assets.OrderBy(a => a.AssetTag)
            .Skip((page.Page - 1) * page.PageSize).Take(page.PageSize)
            .Select(a => new
            {
                a.Id,
                CustodianName = a.CustodianEmployeeId != null
                    ? _db.Employees.Where(e => e.Id == a.CustodianEmployeeId).Select(e => e.DisplayName).FirstOrDefault()
                    : null,
                LocationName = a.LocationId != null
                    ? _db.Locations.Where(l => l.Id == a.LocationId).Select(l => l.Name).FirstOrDefault()
                    : null
            })
            .ToListAsync(cancellationToken);
        var enrichment = rows.ToDictionary(r => r.Id, r => r);

        var items = paged.Items.Select(item =>
            enrichment.TryGetValue(item.Id, out var extra)
                ? new AssetInventoryItem
                {
                    Id = item.Id,
                    AssetType = item.AssetType,
                    Name = item.Name,
                    AssetTag = item.AssetTag,
                    SerialNumber = item.SerialNumber,
                    Status = item.Status,
                    WarrantyExpiry = item.WarrantyExpiry,
                    CustodianEmployeeName = extra.CustodianName,
                    LocationName = extra.LocationName
                }
                : item).ToList();

        return new AssetInventoryResult
        {
            Items = items,
            Page = paged.Page,
            PageSize = paged.PageSize,
            Total = paged.Total,
            OverdueReturns = overdueReturns
        };
    }

    public async Task<AssetInventoryItem> CreateAssetAsync(CreateAssetForm form, CancellationToken cancellationToken = default)
    {
        _currentUser.EnsureActive();

        if (form.WarrantyExpiry is { } warranty && form.PurchaseDate is { } purchase && warranty < purchase)
            throw new RequestValidationException(errors: new()
            {
                ["warrantyExpiry"] = ["Warranty expiry must be on or after the purchase date."]
            });

        var assetTag = string.IsNullOrWhiteSpace(form.AssetTag)
            ? $"AST-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}"
            : form.AssetTag.Trim();

        if (await _db.Set<Asset>().AsNoTracking().AnyAsync(a => a.AssetTag == assetTag, cancellationToken))
            throw new ConflictException($"Asset tag '{assetTag}' is already in use.");

        var asset = new Asset
        {
            Id = Guid.NewGuid(),
            AssetType = form.AssetType,
            Name = form.Name.Trim(),
            AssetTag = assetTag,
            SerialNumber = string.IsNullOrWhiteSpace(form.SerialNumber) ? null : form.SerialNumber.Trim(),
            Model = string.IsNullOrWhiteSpace(form.Model) ? null : form.Model.Trim(),
            PurchaseDate = form.PurchaseDate,
            WarrantyExpiry = form.WarrantyExpiry,
            LocationId = form.LocationId,
            Status = AssetStatus.Available
        };
        _db.Set<Asset>().Add(asset);

        _audit.Add("Asset.Created", nameof(Asset), asset.Id.ToString(),
            details: new { asset.AssetTag, asset.AssetType, asset.Name });
        await _db.SaveChangesAsync(cancellationToken);

        return MapInventoryItem(asset);
    }

    public async Task<AssetAssignmentResult> AssignAsync(Guid id, AssignAssetForm form,
        CancellationToken cancellationToken = default)
    {
        _currentUser.EnsureActive();
        var asset = await _db.Set<Asset>().FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
                    ?? throw new NotFoundException("The asset was not found.");

        // FR-ADM-093: unavailable assets cannot be allocated.
        if (asset.Status != AssetStatus.Available)
            throw new ConflictException($"Asset '{asset.AssetTag}' is not available for assignment (status: {asset.Status}).");

        var employee = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == form.EmployeeId, cancellationToken)
            ?? throw new NotFoundException("The employee was not found.");
        if (!employee.IsActive)
            throw new BusinessRuleException(errors: new()
            {
                ["employeeId"] = ["Assets can only be assigned to active employees."]
            });

        asset.Status = AssetStatus.Assigned;
        asset.CustodianEmployeeId = employee.Id;
        asset.IssuedAt = _clock.UtcNow;
        asset.Condition = form.Condition.Trim();
        asset.Accessories = string.IsNullOrWhiteSpace(form.Accessories) ? null : form.Accessories.Trim();
        asset.AcknowledgedAt = null;
        asset.AcknowledgementNote = null;
        asset.ReturnDueDate = form.ReturnDueDate;
        asset.ReturnRequestedAt = null;
        asset.ReturnRequestReason = null;
        asset.ReturnCondition = null;
        asset.ReturnedAt = null;

        _audit.Add("Asset.Assigned", nameof(Asset), asset.Id.ToString(),
            details: new { asset.AssetTag, employee.EmployeeNumber, form.Condition, form.ReturnDueDate });

        await NotifyUserAsync(employee.Id, "Asset assigned",
            $"{asset.Name} ({asset.AssetTag}) has been assigned to you. Please acknowledge receipt in the app.");

        await _db.SaveChangesAsync(cancellationToken);

        return new AssetAssignmentResult
        {
            AssetId = asset.Id,
            AssetTag = asset.AssetTag,
            EmployeeId = employee.Id,
            IssuedAt = asset.IssuedAt!.Value,
            Condition = asset.Condition ?? string.Empty,
            Accessories = asset.Accessories,
            ReturnDueDate = asset.ReturnDueDate,
            Status = asset.Status
        };
    }

    public async Task<AssetLifecycleResult> ApplyLifecycleActionAsync(Guid id,
        AssetLifecycleRequest request, CancellationToken cancellationToken = default)
    {
        _currentUser.EnsureActive();
        var asset = await _db.Set<Asset>().FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException("The asset was not found.");
        var action = request.Action.Trim();
        var oldStatus = asset.Status;
        var oldCustodian = asset.CustodianEmployeeId;
        var reason = request.Reason.Trim();
        if (reason.Length == 0)
            throw new RequestValidationException("A lifecycle reason is required.",
                new() { ["reason"] = ["Enter why this asset is being transferred or its status changed."] });

        switch (action.ToLowerInvariant())
        {
            case "returntoinventory":
                if (asset.Status is not (AssetStatus.Assigned or AssetStatus.ReturnRequested))
                    throw new ConflictException("Only assigned or return-requested assets can be returned to inventory.");
                asset.Status = AssetStatus.Available;
                asset.CustodianEmployeeId = null;
                asset.ReturnedAt = _clock.UtcNow;
                asset.Condition = NormalizeCondition(request.Condition ?? asset.ReturnCondition);
                asset.ReturnDueDate = null;
                asset.ReturnRequestedAt = null;
                asset.ReturnRequestReason = null;
                asset.ReturnCondition = null;
                break;
            case "sendtorepair":
                if (asset.Status is not (AssetStatus.Assigned or AssetStatus.ReturnRequested))
                    throw new ConflictException("Only assigned or return-requested assets can be sent to repair.");
                asset.Status = AssetStatus.UnderRepair;
                asset.CustodianEmployeeId = null;
                asset.ReturnedAt = _clock.UtcNow;
                asset.Condition = NormalizeCondition(request.Condition ?? asset.ReturnCondition);
                asset.ReturnDueDate = null;
                asset.ReturnRequestedAt = null;
                asset.ReturnRequestReason = null;
                asset.ReturnCondition = null;
                break;
            case "repaircomplete":
                if (asset.Status != AssetStatus.UnderRepair)
                    throw new ConflictException("Only an asset under repair can be marked repaired.");
                asset.Status = AssetStatus.Available;
                asset.Condition = NormalizeCondition(request.Condition);
                break;
            case "marklost":
                if (asset.Status is not (AssetStatus.Assigned or AssetStatus.ReturnRequested or AssetStatus.UnderRepair))
                    throw new ConflictException("This asset cannot be marked lost from its current status.");
                asset.Status = AssetStatus.Lost;
                asset.CustodianEmployeeId = null;
                asset.ReturnDueDate = null;
                break;
            case "markdamaged":
                if (asset.Status is not (AssetStatus.Assigned or AssetStatus.ReturnRequested or AssetStatus.UnderRepair))
                    throw new ConflictException("This asset cannot be marked damaged from its current status.");
                asset.Status = AssetStatus.Damaged;
                asset.CustodianEmployeeId = null;
                asset.ReturnDueDate = null;
                asset.Condition = NormalizeCondition(request.Condition);
                break;
            case "dispose":
                if (asset.Status == AssetStatus.Retired)
                    throw new ConflictException("This asset has already been disposed.");
                asset.Status = AssetStatus.Retired;
                asset.CustodianEmployeeId = null;
                asset.ReturnDueDate = null;
                asset.ReturnedAt = _clock.UtcNow;
                break;
            case "transfer":
                if (asset.Status is not (AssetStatus.Assigned or AssetStatus.ReturnRequested))
                    throw new ConflictException("Only assigned assets can be transferred.");
                if (request.EmployeeId is not { } employeeId)
                    throw new RequestValidationException("A new custodian is required.",
                        new() { ["employeeId"] = ["Choose the employee receiving the transferred asset."] });
                if (asset.CustodianEmployeeId == employeeId)
                    throw new RequestValidationException("The asset is already assigned to this employee.",
                        new() { ["employeeId"] = ["Choose a different employee to transfer the asset."] });
                var employee = await _db.Employees.FirstOrDefaultAsync(item => item.Id == employeeId, cancellationToken)
                    ?? throw new NotFoundException("The receiving employee was not found.");
                if (!employee.IsActive)
                    throw new RequestValidationException("The receiving employee is not active.",
                        new() { ["employeeId"] = ["Assets can only be transferred to an active employee."] });
                asset.Status = AssetStatus.Assigned;
                asset.CustodianEmployeeId = employee.Id;
                asset.IssuedAt = _clock.UtcNow;
                asset.AcknowledgedAt = null;
                asset.AcknowledgementNote = null;
                asset.ReturnRequestedAt = null;
                asset.ReturnRequestReason = null;
                asset.ReturnCondition = null;
                asset.ReturnedAt = null;
                if (!string.IsNullOrWhiteSpace(request.Condition)) asset.Condition = request.Condition.Trim();
                await NotifyUserAsync(employee.Id, "Asset transferred",
                    $"{asset.Name} ({asset.AssetTag}) has been transferred to you. Please acknowledge receipt in the app.");
                break;
            default:
                throw new RequestValidationException("The asset action is not supported.",
                    new() { ["action"] = ["Choose ReturnToInventory, SendToRepair, RepairComplete, MarkLost, MarkDamaged, Dispose or Transfer."] });
        }

        _audit.Add("asset.lifecycle", nameof(Asset), asset.Id.ToString(), details: new
        {
            action,
            reason,
            previousStatus = oldStatus.ToString(),
            currentStatus = asset.Status.ToString(),
            previousCustodianId = oldCustodian,
            currentCustodianId = asset.CustodianEmployeeId,
            condition = asset.Condition
        });
        await _db.SaveChangesAsync(cancellationToken);
        return new AssetLifecycleResult(asset.Id, asset.AssetTag, asset.Status,
            asset.CustodianEmployeeId, action);
    }

    private static string? NormalizeCondition(string? condition) =>
        string.IsNullOrWhiteSpace(condition) ? null : condition.Trim();

    // ---------------------------------------------------------------- helpers

    private Guid RequireEmployeeId() => _currentUser.EmployeeId
        ?? throw new BusinessRuleException("No employee record is linked to your account. Contact HR.");

    private async Task<Asset> MineAsync(Guid id, Guid employeeId, CancellationToken cancellationToken)
    {
        var asset = await _db.Set<Asset>().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        // Someone else's (or unassigned) asset is 404 — no existence leak (FR-EMP-093).
        if (asset is null || asset.CustodianEmployeeId != employeeId ||
            asset.Status is AssetStatus.Available or AssetStatus.Retired)
            throw new NotFoundException("The asset was not found.");
        return asset;
    }

    private async Task NotifyAssetAdminsAsync(string title, string body)
    {
        var adminUserIds = await (
            from ua in _db.UserAccounts.AsNoTracking()
            join ur in _db.UserRoles.AsNoTracking() on ua.Id equals ur.UserId
            join rp in _db.RolePermissions.AsNoTracking() on ur.RoleId equals rp.RoleId
            where rp.PermissionCode == Security.Permissions.AssetManage && ua.Status == Domain.Identity.UserStatus.Active
            select ua.Id)
            .Distinct()
            .ToListAsync();

        foreach (var userId in adminUserIds)
            await _notifications.PublishAsync(new NotificationMessage(
                userId, "Asset", title, body, DeepLink: "/assets/inventory", Category: "Assets"));
    }

    private async Task NotifyUserAsync(Guid employeeId, string title, string body)
    {
        var userId = await _db.UserAccounts.AsNoTracking()
            .Where(u => u.EmployeeId == employeeId && u.Status == Domain.Identity.UserStatus.Active)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync();
        if (userId is null) return;

        await _notifications.PublishAsync(new NotificationMessage(
            userId, "Asset", title, body, DeepLink: "/assets", Category: "Assets", EmployeeId: employeeId));
    }

    private static AssetMineItem MapMine(Asset asset) => new()
    {
        Id = asset.Id,
        AssetType = asset.AssetType,
        Name = asset.Name,
        AssetTag = asset.AssetTag,
        SerialNumber = asset.SerialNumber,
        Model = asset.Model,
        Status = asset.Status,
        IssuedAt = asset.IssuedAt,
        Condition = asset.Condition,
        Accessories = asset.Accessories,
        AcknowledgedAt = asset.AcknowledgedAt,
        ReturnDueDate = asset.ReturnDueDate
    };

    private static AssetInventoryItem MapInventoryItem(Asset asset) => new()
    {
        Id = asset.Id,
        AssetType = asset.AssetType,
        Name = asset.Name,
        AssetTag = asset.AssetTag,
        SerialNumber = asset.SerialNumber,
        Status = asset.Status,
        WarrantyExpiry = asset.WarrantyExpiry
    };
}
