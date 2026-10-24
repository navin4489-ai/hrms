using System.Text.Json;
using Wintech.Hrms.Api.Common.CurrentUser;
using Wintech.Hrms.Api.Common.Registration;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Audit;

namespace Wintech.Hrms.Api.Common.Auditing;

/// <summary>
/// Append-only audit trail (FR-ADM-152/153, BRD section 11).
/// <para>
/// <see cref="Add"/> enqueues an event on the current DbContext so it commits atomically with the
/// business change it describes (call SaveChanges once in your service).
/// <see cref="RecordAsync"/> enqueues and saves immediately — use it for sensitive reads/exports
/// where no other write is happening.
/// </para>
/// </summary>
public interface IAuditService : IAppService
{
    /// <param name="actorOverride">Set during pre-authentication flows (login, password reset) where
    /// ICurrentUser is not yet authenticated; otherwise the current principal is used.</param>
    void Add(string action, string? entityType = null, string? entityId = null,
        string? outcome = "Success", object? details = null, AuditActor? actorOverride = null);

    Task RecordAsync(string action, string? entityType = null, string? entityId = null,
        string? outcome = "Success", object? details = null, AuditActor? actorOverride = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Explicit actor for audit events written outside an authenticated context.</summary>
public sealed record AuditActor(Guid? UserId, string? EmployeeNumber, string? DisplayName);

public sealed class AuditService : IAuditService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly Common.Time.IClock _clock;

    public AuditService(AppDbContext db, ICurrentUser currentUser, Common.Time.IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public void Add(string action, string? entityType = null, string? entityId = null,
        string? outcome = "Success", object? details = null, AuditActor? actorOverride = null)
        => _db.AuditEvents.Add(Build(action, entityType, entityId, outcome, details, actorOverride));

    public async Task RecordAsync(string action, string? entityType = null, string? entityId = null,
        string? outcome = "Success", object? details = null, AuditActor? actorOverride = null,
        CancellationToken cancellationToken = default)
    {
        _db.AuditEvents.Add(Build(action, entityType, entityId, outcome, details, actorOverride));
        await _db.SaveChangesAsync(cancellationToken);
    }

    private AuditEvent Build(string action, string? entityType, string? entityId, string? outcome,
        object? details, AuditActor? actorOverride)
    {
        Guid? actorId = null;
        string? actorNumber = null;
        string? actorName = null;

        if (actorOverride is not null)
        {
            actorId = actorOverride.UserId;
            actorNumber = actorOverride.EmployeeNumber;
            actorName = actorOverride.DisplayName;
        }
        else if (_currentUser.IsAuthenticated)
        {
            actorId = _currentUser.UserId;
            actorNumber = _currentUser.EmployeeNumber;
            actorName = _currentUser.DisplayName;
        }

        return new AuditEvent
        {
            Id = Guid.NewGuid(),
            Timestamp = _clock.UtcNow,
            ActorUserId = actorId,
            ActorEmployeeNumber = actorNumber,
            ActorName = string.IsNullOrWhiteSpace(actorName) ? "Anonymous" : actorName,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Outcome = outcome ?? "Success",
            Details = details is null ? null : JsonSerializer.Serialize(details),
            CorrelationId = _currentUser.CorrelationId,
            IpAddress = _currentUser.RemoteIpAddress
        };
    }
}
