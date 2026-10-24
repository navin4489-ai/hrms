using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.Registration;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Approvals;
using Wintech.Hrms.Api.Domain.Identity;

namespace Wintech.Hrms.Api.Data.Seed;

/// <summary>
/// Demo in-flight approvals (Order 120, after core). Idempotent: a request type is only seeded
/// when no pending approval task exists for it, so restarts never duplicate data and the
/// Leave/Expense/... modules' own seeders take precedence when they run first.
/// </summary>
public sealed class ApprovalsSeedData : IDataSeeder
{
    private readonly AppDbContext _db;
    private readonly IApprovalService _approvals;
    private readonly ILogger<ApprovalsSeedData> _logger;

    public ApprovalsSeedData(AppDbContext db, IApprovalService approvals, ILogger<ApprovalsSeedData> logger)
    {
        _db = db;
        _approvals = approvals;
        _logger = logger;
    }

    public int Order => 120;

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        // Deterministic request ids in a high range so they never collide with core seed ids.
        var demos = new (string RequestType, string UserName, int Seed, string Title, decimal? Amount)[]
        {
            (ApprovalRequestTypes.Leave, "employee2", 1, "Annual leave — Rohit Verma (Nov 3–4)", null),
            (ApprovalRequestTypes.AttendanceCorrection, "employee", 2, "Attendance correction — Priya Nair (Oct 20)", null),
            (ApprovalRequestTypes.Expense, "employee2", 3, "Expense claim — Team offsite", 4500m),
            (ApprovalRequestTypes.Travel, "employee", 4, "Travel request — Mumbai client visit", null),
            (ApprovalRequestTypes.Letter, "employee2", 5, "Employment certificate request", null),
            (ApprovalRequestTypes.ProfileChange, "employee2", 6, "Profile change — bank account update", null),
            (ApprovalRequestTypes.ShiftSwap, "employee", 7, "Shift swap — Oct 28", null),
            (ApprovalRequestTypes.Resignation, "employee2", 8, "Resignation — Rohit Verma", null)
        };

        var seeded = 0;
        foreach (var (requestType, userName, seed, title, amount) in demos)
        {
            // One in-flight demo task per request type; never duplicate on restart.
            if (await _db.ApprovalTasks.AsNoTracking()
                    .AnyAsync(t => t.RequestType == requestType &&
                                   t.Status == nameof(ApprovalStatus.Pending), cancellationToken))
                continue;

            var requester = await _db.UserAccounts.AsNoTracking()
                .Where(u => u.UserName == userName && u.Status == UserStatus.Active)
                .Select(u => new
                {
                    u.Id,
                    u.EmployeeId,
                    Name = u.Employee != null ? u.Employee.DisplayName : u.UserName
                })
                .FirstOrDefaultAsync(cancellationToken);
            if (requester is null)
            {
                _logger.LogWarning("Approvals seed: demo requester {UserName} not found; skipping {RequestType}.",
                    userName, requestType);
                continue;
            }

            var requestId = Guid.Parse($"00000000-0000-0000-0000-{900000 + seed:D12}");
            await _approvals.StartAsync(new ApprovalStartRequest(
                requestType, requestId, title, requester.Id, requester.EmployeeId, requester.Name,
                amount, amount is null ? null : "INR"), cancellationToken);
            seeded++;
        }

        if (seeded > 0)
            _logger.LogInformation("Approvals seed: created {Count} demo approval task(s).", seeded);
    }
}
