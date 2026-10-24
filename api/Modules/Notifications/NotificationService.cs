using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.Auditing;
using Wintech.Hrms.Api.Common.CurrentUser;
using Wintech.Hrms.Api.Common.ErrorHandling;
using Wintech.Hrms.Api.Common.Paging;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Notifications;

namespace Wintech.Hrms.Api.Modules.Notifications;

/// <summary>Notification inbox (contract §16 — FR-EMP-122). Users only ever see their own notifications.</summary>
public sealed class NotificationService : INotificationService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;
    private readonly IClock _clock;

    public NotificationService(AppDbContext db, ICurrentUser currentUser, IAuditService audit, IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
    }

    public async Task<PagedResult<NotificationDto>> ListAsync(bool unreadOnly, PageRequest page,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.RequireUserId();
        var query = _db.Set<AppNotification>().AsNoTracking()
            .Where(n => n.UserId == userId);
        if (unreadOnly) query = query.Where(n => n.ReadAt == null);

        return await query
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new NotificationDto
            {
                Id = n.Id,
                Type = n.Type,
                Title = n.Title,
                Body = n.Body,
                DeepLink = n.DeepLink,
                Category = n.Category,
                CreatedAt = n.CreatedAt,
                ReadAt = n.ReadAt
            })
            .ToPagedAsync(page, item => item, cancellationToken);
    }

    public async Task<UnreadCountResult> UnreadCountAsync(CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.RequireUserId();
        var count = await _db.Set<AppNotification>().AsNoTracking()
            .CountAsync(n => n.UserId == userId && n.ReadAt == null, cancellationToken);
        return new UnreadCountResult(count);
    }

    public async Task MarkReadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.RequireUserId();
        var notification = await _db.Set<AppNotification>()
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("The requested notification was not found.");

        if (notification.ReadAt is null)
        {
            notification.ReadAt = _clock.UtcNow;
            _audit.Add("notification.read", nameof(AppNotification), notification.Id.ToString(), "Success");
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task MarkAllReadAsync(CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.RequireUserId();
        var unread = await _db.Set<AppNotification>()
            .Where(n => n.UserId == userId && n.ReadAt == null)
            .ToListAsync(cancellationToken);
        if (unread.Count == 0) return;

        var now = _clock.UtcNow;
        foreach (var notification in unread) notification.ReadAt = now;
        _audit.Add("notification.read-all", nameof(AppNotification), null, "Success",
            new { count = unread.Count });
        await _db.SaveChangesAsync(cancellationToken);
    }
}
