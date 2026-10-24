using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.Auditing;
using Wintech.Hrms.Api.Common.CurrentUser;
using Wintech.Hrms.Api.Common.ErrorHandling;
using Wintech.Hrms.Api.Common.Notifications;
using Wintech.Hrms.Api.Common.Paging;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Announcements;
using Wintech.Hrms.Api.Domain.Identity;
using Wintech.Hrms.Api.Security;

namespace Wintech.Hrms.Api.Modules.Announcements;

/// <summary>
/// Announcements &amp; acknowledgement workflow (FR-ADM-130..132, FR-EMP-120/123).
/// Drafts become visible through <see cref="PublishAsync"/>; publishing fans an in-app
/// notification out to the targeted population via <see cref="INotificationPublisher"/>.
/// </summary>
public sealed class AnnouncementService : IAnnouncementService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;
    private readonly INotificationPublisher _notifications;
    private readonly IClock _clock;

    public AnnouncementService(AppDbContext db, ICurrentUser currentUser, IAuditService audit,
        IEnumerable<INotificationPublisher> publishers, IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        // The no-op fallback is registered twice (Program.cs + auto-discovery re-registration), which
        // can make plain INotificationPublisher resolution return the no-op — pick the real pipeline.
        _notifications = publishers.FirstOrDefault(p => p is not NoopNotificationPublisher)
            ?? publishers.LastOrDefault()
            ?? throw new InvalidOperationException("No notification publisher is registered.");
        _clock = clock;
    }

    public async Task<PagedResult<AnnouncementDto>> ListAsync(AnnouncementCategory? category, PageRequest page,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId;
        var query = TargetedAtCaller(EffectiveAnnouncements());

        if (category is { } categoryFilter) query = query.Where(a => a.Category == categoryFilter);

        return await Project(query
                .OrderByDescending(a => a.Pinned)
                .ThenByDescending(a => a.PublishedAt), userId)
            .ToPagedAsync(page, item => item, cancellationToken);
    }

    public async Task<AnnouncementDto> GetDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId;

        // Drafts are previewable by their creator and by announcement.create holders.
        var isCreator = await _db.Set<Announcement>().AsNoTracking()
            .AnyAsync(a => a.Id == id && a.CreatedByUserId == userId, cancellationToken);
        var canPreview = isCreator || _currentUser.HasPermission(Permissions.AnnouncementCreate);

        var query = canPreview
            ? _db.Set<Announcement>().AsNoTracking()
            : TargetedAtCaller(EffectiveAnnouncements());

        var announcement = await Project(query.Where(a => a.Id == id), userId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("The requested announcement was not found.");

        return announcement;
    }

    public async Task AcknowledgeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _currentUser.EnsureActive();
        var userId = _currentUser.UserId;
        var announcement = await TargetedAtCaller(EffectiveAnnouncements())
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
            ?? throw new NotFoundException("The requested announcement was not found.");

        if (!announcement.RequiresAcknowledgement)
            throw new ConflictException("This announcement does not require acknowledgement.");

        var already = await _db.Set<AnnouncementAcknowledgement>().AsNoTracking()
            .AnyAsync(x => x.AnnouncementId == id && x.UserId == userId, cancellationToken);
        if (already) throw new ConflictException("You have already acknowledged this announcement.");

        _db.Set<AnnouncementAcknowledgement>().Add(new AnnouncementAcknowledgement
        {
            Id = Guid.NewGuid(),
            AnnouncementId = id,
            UserId = userId,
            EmployeeId = _currentUser.EmployeeId,
            AcknowledgedAt = _clock.UtcNow
        });
        _audit.Add("announcement.acknowledge", nameof(Announcement), id.ToString(), "Success",
            new { announcement.Title });
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<AnnouncementDto> CreateAsync(CreateAnnouncementRequest request, CancellationToken cancellationToken = default)
    {
        _currentUser.EnsureActive();

        if (request.Audience != AnnouncementAudience.All)
        {
            var ids = request.AudienceIds ?? [];
            bool valid = request.Audience == AnnouncementAudience.Department
                ? await _db.OrgUnits.AsNoTracking().CountAsync(u => ids.Contains(u.Id), cancellationToken) == ids.Count
                : await _db.Locations.AsNoTracking().CountAsync(l => ids.Contains(l.Id), cancellationToken) == ids.Count;
            if (!valid)
                throw new RequestValidationException("One or more audience ids do not exist.",
                    new() { ["audienceIds"] = ["One or more audience ids do not exist."] });
        }

        var now = _clock.UtcNow;
        var announcement = new Announcement
        {
            Id = Guid.NewGuid(),
            Title = request.Title.Trim(),
            Summary = request.Summary.Trim(),
            Body = request.Body.Trim(),
            Category = request.Category,
            Audience = request.Audience,
            Pinned = request.Pinned,
            Status = AnnouncementStatus.Draft,
            PublishAt = request.PublishAt,
            ExpiresAt = request.ExpiresAt,
            RequiresAcknowledgement = request.RequiresAcknowledgement,
            CreatedByUserId = _currentUser.UserId,
            CreatedByName = _currentUser.DisplayName,
            CreatedAt = now
        };

        foreach (var targetId in (request.AudienceIds ?? []).Distinct())
            announcement.Targets.Add(new AnnouncementTarget { Id = Guid.NewGuid(), TargetId = targetId });

        _db.Set<Announcement>().Add(announcement);
        _audit.Add("announcement.create", nameof(Announcement), announcement.Id.ToString(), "Success",
            new { announcement.Title, announcement.Category, announcement.Audience });
        await _db.SaveChangesAsync(cancellationToken);

        return await GetDetailAsync(announcement.Id, cancellationToken);
    }

    public async Task<AnnouncementDto> PublishAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _currentUser.EnsureActive();
        var announcement = await _db.Set<Announcement>()
            .Include(a => a.Targets)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
            ?? throw new NotFoundException("The requested announcement was not found.");

        if (announcement.Status != AnnouncementStatus.Draft)
            throw new ConflictException("Only draft announcements can be published.");

        var now = _clock.UtcNow;
        announcement.Status = AnnouncementStatus.Published;
        announcement.PublishedAt = announcement.PublishAt ?? now;

        _audit.Add("announcement.publish", nameof(Announcement), announcement.Id.ToString(), "Success",
            new { announcement.Title, announcement.RequiresAcknowledgement });
        await _db.SaveChangesAsync(cancellationToken);

        await FanOutAsync(announcement, cancellationToken);

        var userId = _currentUser.UserId;
        return await Project(_db.Set<Announcement>().AsNoTracking()
                .Where(a => a.Id == announcement.Id), userId)
            .FirstAsync(cancellationToken);
    }

    // ---------------------------------------------------------------- helpers

    private IQueryable<Announcement> EffectiveAnnouncements()
    {
        var now = _clock.UtcNow;
        return _db.Set<Announcement>().AsNoTracking()
            .Where(a => a.Status == AnnouncementStatus.Published
                && (a.PublishAt == null || a.PublishAt <= now)
                && (a.ExpiresAt == null || a.ExpiresAt > now));
    }

    /// <summary>404 for announcements targeted at another population (contract §16).</summary>
    private IQueryable<Announcement> TargetedAtCaller(IQueryable<Announcement> query)
    {
        var employeeId = _currentUser.EmployeeId;
        if (employeeId is null)
            return query.Where(a => a.Audience == AnnouncementAudience.All);

        var departmentId = _db.Employees.AsNoTracking()
            .Where(e => e.Id == employeeId.Value)
            .Select(e => (Guid?)e.DepartmentId)
            .FirstOrDefault();
        var locationId = _db.Employees.AsNoTracking()
            .Where(e => e.Id == employeeId.Value)
            .Select(e => (Guid?)e.LocationId)
            .FirstOrDefault();

        return query.Where(a => a.Audience == AnnouncementAudience.All
            || (a.Audience == AnnouncementAudience.Department && departmentId != null &&
                a.Targets.Any(t => t.TargetId == departmentId))
            || (a.Audience == AnnouncementAudience.Location && locationId != null &&
                a.Targets.Any(t => t.TargetId == locationId)));
    }

    private static IQueryable<AnnouncementDto> Project(IQueryable<Announcement> query, Guid userId) =>
        query.Select(a => new AnnouncementDto
        {
            Id = a.Id,
            Title = a.Title,
            Summary = a.Summary,
            Body = a.Body,
            Category = a.Category,
            Audience = a.Audience,
            Pinned = a.Pinned,
            Status = a.Status,
            PublishAt = a.PublishAt,
            PublishedAt = a.PublishedAt,
            ExpiresAt = a.ExpiresAt,
            RequiresAcknowledgement = a.RequiresAcknowledgement,
            AcknowledgedAt = a.Acknowledgements
                .Where(x => x.UserId == userId)
                .Select(x => (DateTime?)x.AcknowledgedAt)
                .FirstOrDefault()
        });

    /// <summary>In-app fan-out to the targeted population (FR-EMP-120, FR-ADM-133).</summary>
    private async Task FanOutAsync(Announcement announcement, CancellationToken cancellationToken)
    {
        var targets = announcement.Targets.Select(t => t.TargetId).ToList();

        var users = from ua in _db.UserAccounts.AsNoTracking()
                    where ua.Status == UserStatus.Active
                    select new
                    {
                        ua.Id,
                        ua.EmployeeId,
                        DepartmentId = ua.Employee != null ? (Guid?)ua.Employee.DepartmentId : null,
                        LocationId = ua.Employee != null ? (Guid?)ua.Employee.LocationId : null
                    };

        users = announcement.Audience switch
        {
            AnnouncementAudience.Department => users.Where(u => u.DepartmentId != null && targets.Contains(u.DepartmentId.Value)),
            AnnouncementAudience.Location => users.Where(u => u.LocationId != null && targets.Contains(u.LocationId.Value)),
            _ => users
        };

        var recipients = await users.ToListAsync(cancellationToken);
        foreach (var recipient in recipients)
        {
            await _notifications.PublishAsync(new NotificationMessage(
                recipient.Id,
                "AnnouncementPublished",
                announcement.Title,
                announcement.Summary,
                $"/announcements/{announcement.Id}",
                "Announcement",
                false,
                recipient.EmployeeId), cancellationToken);
        }
    }
}
