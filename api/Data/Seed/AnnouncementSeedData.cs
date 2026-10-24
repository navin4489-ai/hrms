using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.Registration;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Announcements;
using Wintech.Hrms.Api.Domain.Cases;
using Wintech.Hrms.Api.Domain.Notifications;

namespace Wintech.Hrms.Api.Data.Seed;

/// <summary>
/// Idempotent announcement seed (FR-EMP-123, FR-ADM-130..132): published announcements across
/// categories — one pinned, one mandatory with an acknowledgement already recorded by a demo user —
/// plus a location-targeted notice and a few demo inbox notifications for the employee account.
/// </summary>
public sealed class AnnouncementSeedData : IDataSeeder
{
    private readonly AppDbContext _db;
    private readonly ILogger<AnnouncementSeedData> _logger;

    public AnnouncementSeedData(AppDbContext db, ILogger<AnnouncementSeedData> logger)
    {
        _db = db;
        _logger = logger;
    }

    public int Order => 141;

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var seededAnnouncements = await SeedAnnouncementsAsync(cancellationToken);
        await SeedDemoNotificationsAsync(cancellationToken);

        if (seededAnnouncements)
            _logger.LogInformation("Seeded announcements and demo notifications.");
    }

    private async Task<bool> SeedAnnouncementsAsync(CancellationToken cancellationToken)
    {
        if (await _db.Set<Announcement>().AnyAsync(cancellationToken)) return false;

        var now = DateTime.UtcNow;
        var announcements = new List<Announcement>();

        // 1. Pinned company announcement (All)
        announcements.Add(new Announcement
        {
            Id = Guid.Parse("00000000-0000-0000-0010-000000000001"),
            Title = "Annual town hall — save the date",
            Summary = "The company town hall is on 7 November, 4 PM IST at Bengaluru HQ and livestreamed.",
            Body = "Join the annual town hall for business updates, awards and an open Q&A with leadership. "
                 + "Remote colleagues can join the livestream; submit questions in advance from the Events page.",
            Category = AnnouncementCategory.Company,
            Audience = AnnouncementAudience.All,
            Pinned = true,
            Status = AnnouncementStatus.Published,
            PublishedAt = now.AddDays(-2),
            RequiresAcknowledgement = false,
            CreatedByName = "Meera Iyer",
            CreatedAt = now.AddDays(-2)
        });

        // 2. Mandatory policy announcement, already acknowledged by the "employee" demo user (FR-ADM-132)
        var policyAnnouncement = new Announcement
        {
            Id = Guid.Parse("00000000-0000-0000-0010-000000000002"),
            Title = "Revised travel & expense policy — acknowledgement required",
            Summary = "The updated travel and expense policy is effective 1 November. Please acknowledge after reading.",
            Body = "Key changes: economy-only flights under 6 hours, hotel caps per city, and receipts mandatory for "
                 + "all claims above ₹2,000. Claims must be submitted within 30 days of the expense date. "
                 + "Read the full policy in Employee Documents and acknowledge below.",
            Category = AnnouncementCategory.Policy,
            Audience = AnnouncementAudience.All,
            Pinned = false,
            Status = AnnouncementStatus.Published,
            PublishedAt = now.AddDays(-4),
            RequiresAcknowledgement = true,
            CreatedByName = "Meera Iyer",
            CreatedAt = now.AddDays(-4)
        };
        announcements.Add(policyAnnouncement);

        // 3. Wellness announcement (All)
        announcements.Add(new Announcement
        {
            Id = Guid.Parse("00000000-0000-0000-0010-000000000003"),
            Title = "Free health check-up camp this week",
            Summary = "Free health check-ups and flu shots at Bengaluru HQ on 28–29 October, 9 AM to 3 PM.",
            Body = "Walk in with your employee ID for a free health check-up (BP, sugar, cholesterol) and a seasonal "
                 + "flu shot. Slots are first-come-first-served at the ground-floor conference room.",
            Category = AnnouncementCategory.Wellness,
            Audience = AnnouncementAudience.All,
            Pinned = false,
            Status = AnnouncementStatus.Published,
            PublishedAt = now.AddDays(-1),
            RequiresAcknowledgement = false,
            CreatedByName = "Sandeep Rao",
            CreatedAt = now.AddDays(-1)
        });

        // 4. Location-targeted event (FR-ADM-131): Bengaluru office only
        var blrLocationId = await _db.Locations.AsNoTracking()
            .Where(l => l.Code == "BLR")
            .Select(l => (Guid?)l.Id)
            .FirstOrDefaultAsync(cancellationToken) ?? Guid.Parse("00000000-0000-0000-0000-000000000020");

        var drill = new Announcement
        {
            Id = Guid.Parse("00000000-0000-0000-0010-000000000004"),
            Title = "Bengaluru office evacuation drill",
            Summary = "A fire evacuation drill will run on 30 October at 11 AM for Bengaluru HQ occupants.",
            Body = "When the alarm sounds, leave via the nearest stairwell and assemble at the designated muster point. "
                 + "Do not use lifts. Floor marshals will guide you; the drill lasts about 20 minutes.",
            Category = AnnouncementCategory.Event,
            Audience = AnnouncementAudience.Location,
            Pinned = false,
            Status = AnnouncementStatus.Published,
            PublishedAt = now.AddDays(-0.5),
            RequiresAcknowledgement = false,
            CreatedByName = "Sandeep Rao",
            CreatedAt = now.AddDays(-0.5)
        };
        drill.Targets.Add(new AnnouncementTarget { Id = Guid.NewGuid(), TargetId = blrLocationId });
        announcements.Add(drill);

        _db.Set<Announcement>().AddRange(announcements);

        // The "employee" demo user already acknowledged the mandatory policy announcement.
        var employeeAccount = await _db.UserAccounts.AsNoTracking()
            .Where(u => u.UserName == "employee")
            .Select(u => new { u.Id, u.EmployeeId })
            .FirstOrDefaultAsync(cancellationToken);
        if (employeeAccount is not null)
        {
            _db.Set<AnnouncementAcknowledgement>().Add(new AnnouncementAcknowledgement
            {
                Id = Guid.NewGuid(),
                AnnouncementId = policyAnnouncement.Id,
                UserId = employeeAccount.Id,
                EmployeeId = employeeAccount.EmployeeId,
                AcknowledgedAt = now.AddDays(-3)
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task SeedDemoNotificationsAsync(CancellationToken cancellationToken)
    {
        if (await _db.Set<AppNotification>().AnyAsync(cancellationToken)) return;

        var now = DateTime.UtcNow;
        var employeeAccount = await _db.UserAccounts.AsNoTracking()
            .Where(u => u.UserName == "employee")
            .Select(u => new { u.Id, u.EmployeeId })
            .FirstOrDefaultAsync(cancellationToken);
        if (employeeAccount is null) return;

        var caseId = await _db.Set<HelpdeskCase>().AsNoTracking()
            .Where(c => c.TicketNumber.StartsWith($"CS-{now.Year:D4}-"))
            .OrderBy(c => c.TicketNumber)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var notifications = new List<AppNotification>
        {
            new()
            {
                Id = Guid.NewGuid(), UserId = employeeAccount.Id, EmployeeId = employeeAccount.EmployeeId,
                Type = "AnnouncementPublished",
                Title = "Revised travel & expense policy — acknowledgement required",
                Body = "The updated travel and expense policy is effective 1 November. Please acknowledge after reading.",
                DeepLink = "/announcements/00000000-0000-0000-0010-000000000002",
                Category = "Announcement", Sensitive = false,
                CreatedAt = now.AddDays(-4), ReadAt = null
            },
            new()
            {
                Id = Guid.NewGuid(), UserId = employeeAccount.Id, EmployeeId = employeeAccount.EmployeeId,
                Type = "PayslipPublished",
                Title = "Your payslip is available",
                Body = "The payslip for the latest period is ready. Open the app to download it.",
                DeepLink = "/payslips",
                Category = "Payroll", Sensitive = false,
                CreatedAt = now.AddDays(-2), ReadAt = null
            }
        };

        if (caseId is not null)
        {
            notifications.Add(new AppNotification
            {
                Id = Guid.NewGuid(), UserId = employeeAccount.Id, EmployeeId = employeeAccount.EmployeeId,
                Type = "CaseReplied",
                Title = "Update on your helpdesk case",
                Body = "HR has replied to your helpdesk case. Open the app to read the update.",
                DeepLink = $"/cases/{caseId}",
                Category = "Case", Sensitive = false,
                CreatedAt = now.AddHours(-6), ReadAt = null
            });
        }

        _db.Set<AppNotification>().AddRange(notifications);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
