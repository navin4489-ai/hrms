using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.Dashboard;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Announcements;
using Wintech.Hrms.Api.Domain.Notifications;

namespace Wintech.Hrms.Api.Modules.Notifications;

/// <summary>
/// Contributes the engagement slice of <c>GET /me/dashboard</c> (contract §5):
/// <c>announcements</c> (top 5 recent for the caller) and <c>notificationsUnread</c> (count).
/// </summary>
public sealed class EngagementDashboardSliceProvider : IDashboardSliceProvider
{
    private readonly AppDbContext _db;
    private readonly IClock _clock;

    public EngagementDashboardSliceProvider(AppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public int Order => 110;

    public async Task<IReadOnlyDictionary<string, object?>> GetSliceAsync(DashboardContext context,
        CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;

        var announcementQuery = _db.Set<Announcement>().AsNoTracking()
            .Where(a => a.Status == AnnouncementStatus.Published
                && (a.PublishAt == null || a.PublishAt <= now)
                && (a.ExpiresAt == null || a.ExpiresAt > now));

        if (context.EmployeeId is { } employeeId)
        {
            var departmentId = await _db.Employees.AsNoTracking()
                .Where(e => e.Id == employeeId)
                .Select(e => (Guid?)e.DepartmentId)
                .FirstOrDefaultAsync(cancellationToken);
            var locationId = await _db.Employees.AsNoTracking()
                .Where(e => e.Id == employeeId)
                .Select(e => (Guid?)e.LocationId)
                .FirstOrDefaultAsync(cancellationToken);

            announcementQuery = announcementQuery.Where(a =>
                a.Audience == AnnouncementAudience.All
                || (a.Audience == AnnouncementAudience.Department && departmentId != null &&
                    a.Targets.Any(t => t.TargetId == departmentId))
                || (a.Audience == AnnouncementAudience.Location && locationId != null &&
                    a.Targets.Any(t => t.TargetId == locationId)));
        }
        else
        {
            announcementQuery = announcementQuery.Where(a => a.Audience == AnnouncementAudience.All);
        }

        var announcements = await announcementQuery
            .OrderByDescending(a => a.Pinned)
            .ThenByDescending(a => a.PublishedAt)
            .Take(5)
            .Select(a => new
            {
                a.Id, a.Title, a.Summary, a.Category, a.PublishedAt, a.RequiresAcknowledgement
            })
            .ToListAsync(cancellationToken);

        var unread = await _db.Set<AppNotification>().AsNoTracking()
            .CountAsync(n => n.UserId == context.UserId && n.ReadAt == null, cancellationToken);

        return new Dictionary<string, object?>
        {
            ["announcements"] = announcements,
            ["notificationsUnread"] = unread
        };
    }
}
