using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using Wintech.Hrms.Api.Common.Registration;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Approvals;
using Wintech.Hrms.Api.Domain.Expenses;

namespace Wintech.Hrms.Api.Data.Seed;

/// <summary>
/// Demo expense claims (across the full status spectrum) and travel requests for the seeded
/// demo employees, each with a matching approval task (Order 130 — after CoreSeeder).
/// Idempotent: skipped entirely once any claim exists.
/// </summary>
public sealed class ExpenseSeedData : IDataSeeder
{
    private readonly AppDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<ExpenseSeedData> _logger;

    public ExpenseSeedData(AppDbContext db, IClock clock, ILogger<ExpenseSeedData> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    public int Order => 130;

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await _db.Set<ExpenseClaim>().AsNoTracking().AnyAsync(cancellationToken))
        {
            _logger.LogDebug("Expense seed data already present; skipping.");
            return;
        }

        // Resolve demo employees by number so the seed never depends on GUID internals.
        var employees = await _db.Employees.AsNoTracking()
            .Where(e => e.EmployeeNumber == "EMP006" || e.EmployeeNumber == "EMP007" || e.EmployeeNumber == "EMP002")
            .ToDictionaryAsync(e => e.EmployeeNumber, e => e.Id, cancellationToken);
        var priya = employees["EMP006"]; // employee
        var rohit = employees["EMP007"]; // employee2
        var anil = employees["EMP002"]; // manager

        var requesterUsers = await _db.UserAccounts.AsNoTracking()
            .Where(u => u.EmployeeId != null)
            .ToDictionaryAsync(u => u.EmployeeId!.Value, u => u.Id, cancellationToken);

        Guid UserFor(Guid employeeId) => requesterUsers.GetValueOrDefault(employeeId, Guid.NewGuid());

        ApprovalTask AddApproval(ExpenseClaim claim, string status,
            int step = 1, DateTime? completedAt = null)
        {
            var task = new ApprovalTask
            {
                Id = Guid.NewGuid(),
                RequestType = "Expense",
                RequestId = claim.Id,
                Title = $"Expense claim — {claim.Category} {claim.Amount:N0} {claim.Currency}",
                RequesterUserId = UserFor(claim.EmployeeId),
                RequesterEmployeeId = claim.EmployeeId,
                Amount = claim.Amount,
                Currency = claim.Currency,
                Status = status,
                CurrentStepNumber = step,
                CurrentStepName = step >= 2 ? "Finance review" : "Line manager",
                CreatedAt = claim.SubmittedAt,
                UpdatedAt = completedAt ?? claim.SubmittedAt,
                CompletedAt = completedAt,
                Version = 1
            };
            _db.ApprovalTasks.Add(task);
            claim.ApprovalTaskId = task.Id;
            return task;
        }

        var claims = new List<ExpenseClaim>();

        var approvedMeals = new ExpenseClaim
        {
            Id = Guid.Parse("ee010000-0000-0000-0000-000000000001"),
            EmployeeId = priya,
            Category = "Meals",
            Amount = 1250m,
            Currency = "INR",
            ExpenseDate = new DateOnly(2026, 9, 12),
            BusinessPurpose = "Team lunch during the payments v2 design workshop",
            CostCentre = "ENG-01",
            Status = ExpenseClaimStatus.Approved,
            SubmittedAt = new DateTime(2026, 9, 12, 11, 30, 0, DateTimeKind.Utc),
            ApprovedAt = new DateTime(2026, 9, 15, 9, 0, 0, DateTimeKind.Utc),
            ReceiptFileName = "team-lunch.pdf",
            ReceiptContentType = "application/pdf",
            ReceiptSizeBytes = 48_213,
            ReceiptSha256 = SeedHash("approved-meals")
        };
        claims.Add(approvedMeals);

        var pendingTravel = new ExpenseClaim
        {
            Id = Guid.Parse("ee010000-0000-0000-0000-000000000002"),
            EmployeeId = priya,
            Category = "Travel",
            Amount = 8500m,
            Currency = "INR",
            ExpenseDate = new DateOnly(2026, 10, 10),
            BusinessPurpose = "Cab to and from the Mumbai client workshop",
            CostCentre = "ENG-01",
            Status = ExpenseClaimStatus.Pending,
            SubmittedAt = new DateTime(2026, 10, 10, 18, 5, 0, DateTimeKind.Utc),
            ReceiptFileName = "cab-receipt.pdf",
            ReceiptContentType = "application/pdf",
            ReceiptSizeBytes = 31_884,
            ReceiptSha256 = SeedHash("pending-travel")
        };
        claims.Add(pendingTravel);

        var rejectedTransport = new ExpenseClaim
        {
            Id = Guid.Parse("ee010000-0000-0000-0000-000000000003"),
            EmployeeId = rohit,
            Category = "LocalTransport",
            Amount = 450m,
            Currency = "INR",
            ExpenseDate = new DateOnly(2026, 10, 2),
            BusinessPurpose = "Auto rickshaw to the Mumbai office for the release drill",
            CostCentre = "ENG-01",
            Status = ExpenseClaimStatus.Rejected,
            SubmittedAt = new DateTime(2026, 10, 2, 20, 15, 0, DateTimeKind.Utc),
            RejectionReason = "Missing original invoice — please re-submit with the GST invoice.",
            ReceiptFileName = "auto-ride.jpg",
            ReceiptContentType = "image/jpeg",
            ReceiptSizeBytes = 202_114,
            ReceiptSha256 = SeedHash("rejected-transport")
        };
        claims.Add(rejectedTransport);

        var paidSupplies = new ExpenseClaim
        {
            Id = Guid.Parse("ee010000-0000-0000-0000-000000000004"),
            EmployeeId = priya,
            Category = "OfficeSupplies",
            Amount = 2200m,
            Currency = "INR",
            ExpenseDate = new DateOnly(2026, 8, 5),
            BusinessPurpose = "Ergonomic keyboard and monitor arm for the home workstation",
            CostCentre = "ENG-01",
            Status = ExpenseClaimStatus.Paid,
            SubmittedAt = new DateTime(2026, 8, 5, 10, 0, 0, DateTimeKind.Utc),
            ApprovedAt = new DateTime(2026, 8, 8, 9, 0, 0, DateTimeKind.Utc),
            PaidReference = "EXP-2026-0042",
            ReceiptFileName = "invoice-4471.pdf",
            ReceiptContentType = "application/pdf",
            ReceiptSizeBytes = 96_551,
            ReceiptSha256 = SeedHash("paid-supplies")
        };
        claims.Add(paidSupplies);

        var managerMeals = new ExpenseClaim
        {
            Id = Guid.Parse("ee010000-0000-0000-0000-000000000005"),
            EmployeeId = anil,
            Category = "Meals",
            Amount = 900m,
            Currency = "INR",
            ExpenseDate = new DateOnly(2026, 10, 20),
            BusinessPurpose = "Working dinner with the engineering team during the release",
            CostCentre = "ENG-01",
            Status = ExpenseClaimStatus.Pending,
            SubmittedAt = new DateTime(2026, 10, 20, 21, 40, 0, DateTimeKind.Utc),
            ReceiptFileName = "dinner-bill.pdf",
            ReceiptContentType = "application/pdf",
            ReceiptSizeBytes = 55_010,
            ReceiptSha256 = SeedHash("manager-meals")
        };
        claims.Add(managerMeals);

        _db.Set<ExpenseClaim>().AddRange(claims);

        AddApproval(approvedMeals, nameof(ApprovalStatus.Approved), step: 2,
            completedAt: approvedMeals.ApprovedAt);
        AddApproval(pendingTravel, nameof(ApprovalStatus.Pending));
        AddApproval(rejectedTransport, nameof(ApprovalStatus.Rejected),
            completedAt: rejectedTransport.SubmittedAt.AddDays(2));
        AddApproval(paidSupplies, nameof(ApprovalStatus.Approved), step: 2,
            completedAt: paidSupplies.ApprovedAt);
        AddApproval(managerMeals, nameof(ApprovalStatus.Pending));

        // ---- travel requests ------------------------------------------------
        var travel = new List<TravelRequest>();

        var mumbaiTrip = new TravelRequest
        {
            Id = Guid.Parse("ee010000-0000-0000-0000-000000000101"),
            EmployeeId = priya,
            Destination = "Mumbai",
            Purpose = "On-site client workshop for the payments v2 integration",
            StartDate = new DateOnly(2026, 11, 3),
            EndDate = new DateOnly(2026, 11, 6),
            EstimatedCost = 18000m,
            Currency = "INR",
            Status = TravelRequestStatus.Pending,
            SubmittedAt = new DateTime(2026, 10, 18, 8, 20, 0, DateTimeKind.Utc)
        };
        travel.Add(mumbaiTrip);

        var bengaluruTrip = new TravelRequest
        {
            Id = Guid.Parse("ee010000-0000-0000-0000-000000000102"),
            EmployeeId = rohit,
            Destination = "Bengaluru",
            Purpose = "Quarterly engineering summit at the headquarters",
            StartDate = new DateOnly(2026, 9, 15),
            EndDate = new DateOnly(2026, 9, 17),
            EstimatedCost = 9500m,
            Currency = "INR",
            Status = TravelRequestStatus.Approved,
            SubmittedAt = new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc),
            DecidedAt = new DateTime(2026, 9, 3, 14, 0, 0, DateTimeKind.Utc)
        };
        travel.Add(bengaluruTrip);

        var delhiTrip = new TravelRequest
        {
            Id = Guid.Parse("ee010000-0000-0000-0000-000000000103"),
            EmployeeId = priya,
            Destination = "New Delhi",
            Purpose = "Government liaison meeting for the compliance filing",
            StartDate = new DateOnly(2026, 8, 20),
            EndDate = new DateOnly(2026, 8, 22),
            EstimatedCost = 12000m,
            Currency = "INR",
            Status = TravelRequestStatus.Rejected,
            SubmittedAt = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc),
            DecidedAt = new DateTime(2026, 8, 12, 10, 0, 0, DateTimeKind.Utc),
            RejectionReason = "Coincides with the release freeze window — please re-plan for the following sprint."
        };
        travel.Add(delhiTrip);

        _db.Set<TravelRequest>().AddRange(travel);

        foreach (var request in travel)
        {
            var status = request.Status switch
            {
                TravelRequestStatus.Approved => nameof(ApprovalStatus.Approved),
                TravelRequestStatus.Rejected => nameof(ApprovalStatus.Rejected),
                _ => nameof(ApprovalStatus.Pending)
            };
            var task = new ApprovalTask
            {
                Id = Guid.NewGuid(),
                RequestType = "Travel",
                RequestId = request.Id,
                Title = $"Travel request — {request.Destination} ({request.StartDate:yyyy-MM-dd})",
                RequesterUserId = UserFor(request.EmployeeId),
                RequesterEmployeeId = request.EmployeeId,
                Amount = request.EstimatedCost,
                Currency = request.Currency,
                Status = status,
                CurrentStepNumber = status == nameof(ApprovalStatus.Approved) ? 2 : 1,
                CurrentStepName = status == nameof(ApprovalStatus.Approved) ? "Finance review" : "Line manager",
                CreatedAt = request.SubmittedAt,
                UpdatedAt = request.DecidedAt ?? request.SubmittedAt,
                CompletedAt = request.DecidedAt,
                Version = 1
            };
            _db.ApprovalTasks.Add(task);
            request.ApprovalTaskId = task.Id;
        }

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Seeded {Claims} expense claims and {Travel} travel requests.",
            claims.Count, travel.Count);
    }

    private static string SeedHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
