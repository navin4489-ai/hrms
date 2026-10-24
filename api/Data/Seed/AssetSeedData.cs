using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.Registration;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Assets;

namespace Wintech.Hrms.Api.Data.Seed;

/// <summary>
/// Demo IT asset inventory (Order 131): available stock, acknowledged assignments,
/// one unacknowledged issue (for the acknowledge flow) and one overdue return (FR-ADM-093).
/// Idempotent: skipped once any asset exists.
/// </summary>
public sealed class AssetSeedData : IDataSeeder
{
    private readonly AppDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<AssetSeedData> _logger;

    public AssetSeedData(AppDbContext db, IClock clock, ILogger<AssetSeedData> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    public int Order => 131;

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await _db.Set<Asset>().AsNoTracking().AnyAsync(cancellationToken))
        {
            _logger.LogDebug("Asset seed data already present; skipping.");
            return;
        }

        var employees = await _db.Employees.AsNoTracking()
            .Where(e => e.EmployeeNumber == "EMP006" || e.EmployeeNumber == "EMP007" ||
                        e.EmployeeNumber == "EMP002" || e.EmployeeNumber == "EMP001")
            .ToDictionaryAsync(e => e.EmployeeNumber, e => e.Id, cancellationToken);

        var locations = await _db.Locations.AsNoTracking()
            .Where(l => l.Code == "BLR" || l.Code == "MUM")
            .ToDictionaryAsync(l => l.Code, l => l.Id, cancellationToken);

        Guid Loc(string code) => locations.GetValueOrDefault(code);

        var assets = new List<Asset>
        {
            // Assigned + acknowledged laptop (Priya).
            new()
            {
                Id = Guid.Parse("aa010000-0000-0000-0000-000000000001"),
                AssetType = AssetType.Laptop,
                Name = "MacBook Pro 14\"",
                AssetTag = "AST-LAP-001",
                SerialNumber = "C02XK1T9MD6T",
                Model = "MacBook Pro 14 (M3 Pro)",
                PurchaseDate = new DateOnly(2025, 11, 20),
                WarrantyExpiry = new DateOnly(2028, 11, 19),
                LocationId = Loc("BLR"),
                Status = AssetStatus.Assigned,
                CustodianEmployeeId = employees["EMP006"],
                IssuedAt = new DateTime(2026, 1, 10, 9, 0, 0, DateTimeKind.Utc),
                Condition = "Good",
                Accessories = "Docking station, Wireless mouse",
                AcknowledgedAt = new DateTime(2026, 1, 10, 10, 15, 0, DateTimeKind.Utc)
            },
            // Overdue return: requested by Rohit, due date already passed (FR-ADM-093).
            new()
            {
                Id = Guid.Parse("aa010000-0000-0000-0000-000000000002"),
                AssetType = AssetType.Phone,
                Name = "iPhone 14",
                AssetTag = "AST-PHN-001",
                SerialNumber = "F2LQH8M7J6VN",
                Model = "iPhone 14 (128 GB)",
                PurchaseDate = new DateOnly(2025, 3, 1),
                WarrantyExpiry = new DateOnly(2027, 2, 28),
                LocationId = Loc("MUM"),
                Status = AssetStatus.ReturnRequested,
                CustodianEmployeeId = employees["EMP007"],
                IssuedAt = new DateTime(2026, 2, 1, 9, 30, 0, DateTimeKind.Utc),
                Condition = "Good",
                Accessories = "Case, Screen protector",
                AcknowledgedAt = new DateTime(2026, 2, 2, 9, 5, 0, DateTimeKind.Utc),
                ReturnDueDate = new DateOnly(2026, 10, 14),
                ReturnRequestedAt = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc),
                ReturnRequestReason = "Upgrading to the new company phone model",
                ReturnCondition = "Good"
            },
            // Available stock.
            new()
            {
                Id = Guid.Parse("aa010000-0000-0000-0000-000000000003"),
                AssetType = AssetType.Laptop,
                Name = "Dell Latitude 5540",
                AssetTag = "AST-LAP-002",
                SerialNumber = "DL5540-IND-0042",
                Model = "Latitude 5540 (i7, 16 GB)",
                PurchaseDate = new DateOnly(2026, 4, 10),
                WarrantyExpiry = new DateOnly(2029, 4, 9),
                LocationId = Loc("BLR"),
                Status = AssetStatus.Available
            },
            // Badge issued to the line manager (Anil).
            new()
            {
                Id = Guid.Parse("aa010000-0000-0000-0000-000000000004"),
                AssetType = AssetType.Badge,
                Name = "Office access badge",
                AssetTag = "AST-BDG-001",
                SerialNumber = "BADGE-0002",
                LocationId = Loc("BLR"),
                Status = AssetStatus.Assigned,
                CustodianEmployeeId = employees["EMP002"],
                IssuedAt = new DateTime(2025, 1, 15, 9, 0, 0, DateTimeKind.Utc),
                Condition = "Good",
                AcknowledgedAt = new DateTime(2025, 1, 15, 9, 20, 0, DateTimeKind.Utc)
            },
            // Available SIM.
            new()
            {
                Id = Guid.Parse("aa010000-0000-0000-0000-000000000005"),
                AssetType = AssetType.Sim,
                Name = "Postpaid SIM (data)",
                AssetTag = "AST-SIM-001",
                Model = "5G enterprise plan",
                LocationId = Loc("BLR"),
                Status = AssetStatus.Available
            },
            // Just issued to Priya, not yet acknowledged — used to exercise the acknowledge flow.
            new()
            {
                Id = Guid.Parse("aa010000-0000-0000-0000-000000000006"),
                AssetType = AssetType.Accessory,
                Name = "Jabra Evolve2 65 Headset",
                AssetTag = "AST-ACC-001",
                SerialNumber = "JAB-65-0117",
                Model = "Evolve2 65",
                PurchaseDate = new DateOnly(2026, 9, 25),
                WarrantyExpiry = new DateOnly(2028, 9, 24),
                LocationId = Loc("BLR"),
                Status = AssetStatus.Assigned,
                CustodianEmployeeId = employees["EMP006"],
                IssuedAt = new DateTime(2026, 10, 1, 8, 45, 0, DateTimeKind.Utc),
                Condition = "Sealed"
            }
        };

        _db.Set<Asset>().AddRange(assets);
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Seeded {Assets} demo assets (1 overdue return).", assets.Count);
    }
}
