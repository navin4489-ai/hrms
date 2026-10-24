using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.Notifications;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Identity;
using Wintech.Hrms.Api.Domain.Notifications;

namespace Wintech.Hrms.Api.Modules.Notifications;

/// <summary>
/// The real notification pipeline (FR-EMP-120/122, BR-013). Persists every published message as an
/// in-app inbox row, fans out to the target user (resolving <c>EmployeeId</c> when only that is known)
/// and guarantees that a <c>Sensitive</c> payload never carries its detail — the deep link leads to the
/// authoritative record behind authentication.
/// <para>
/// Registered automatically through <c>IAppService</c> after Program.cs's no-op fallback, so it wins
/// (last registration wins) without any manual wiring.
/// </para>
/// </summary>
public sealed class NotificationPublisher : INotificationPublisher
{
    private const string GenericSensitiveBody =
        "You have a new confidential notification. Open the app to view details.";

    private readonly AppDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<NotificationPublisher> _logger;

    public NotificationPublisher(AppDbContext db, IClock clock, ILogger<NotificationPublisher> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    public async Task PublishAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        var userId = message.UserId;
        Guid? employeeId = message.EmployeeId;

        if (userId is null && employeeId is not null)
        {
            // Fan out by employee: resolve the active login account(s).
            userId = await _db.UserAccounts.AsNoTracking()
                .Where(u => u.EmployeeId == employeeId.Value && u.Status == UserStatus.Active)
                .Select(u => (Guid?)u.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }
        else if (userId is not null && employeeId is null)
        {
            employeeId = await _db.UserAccounts.AsNoTracking()
                .Where(u => u.Id == userId.Value)
                .Select(u => u.EmployeeId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (userId is null)
        {
            _logger.LogWarning("Notification ({Type}) dropped: no active user for the target {Employee}.",
                message.Type, message.EmployeeId?.ToString() ?? "(none)");
            return;
        }

        // BR-013: sensitive detail is never stored in the notification payload — the inbox row keeps a
        // generic body and the full context stays server-side behind the deep link.
        var body = message.Sensitive ? GenericSensitiveBody : message.Body;

        _db.Set<AppNotification>().Add(new AppNotification
        {
            Id = Guid.NewGuid(),
            UserId = userId.Value,
            EmployeeId = employeeId,
            Type = Truncate(message.Type, 100),
            Title = Truncate(message.Title, 300),
            Body = Truncate(body, 1000),
            DeepLink = message.DeepLink is null ? null : Truncate(message.DeepLink, 500),
            Category = string.IsNullOrWhiteSpace(message.Category) ? "System" : Truncate(message.Category, 50),
            Sensitive = message.Sensitive,
            CreatedAt = _clock.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
