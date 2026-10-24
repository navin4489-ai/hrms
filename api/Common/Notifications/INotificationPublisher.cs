using Wintech.Hrms.Api.Common.Registration;

namespace Wintech.Hrms.Api.Common.Notifications;

/// <summary>
/// Cross-cutting notification fan-out used by every module (leave decisions, approvals assigned,
/// payslip published, ticket updates, document expiry — FR-EMP-120).
/// <para>
/// A no-op fallback is registered first in Program.cs; the real in-app implementation is supplied
/// by the Notifications module and takes precedence automatically (last registration wins).
/// </para>
/// </summary>
public sealed record NotificationMessage(
    Guid? UserId,
    string Type,
    string Title,
    string Body,
    string? DeepLink = null,
    string Category = "System",
    /// <summary>True when the payload must not appear on a lock screen (BR-013).</summary>
    bool Sensitive = false,
    Guid? EmployeeId = null);

public interface INotificationPublisher : IAppService
{
    Task PublishAsync(NotificationMessage message, CancellationToken cancellationToken = default);
}

/// <summary>Registered as a fallback so the API runs even before the Notifications module lands.</summary>
public sealed class NoopNotificationPublisher : INotificationPublisher
{
    private readonly ILogger<NoopNotificationPublisher> _logger;

    public NoopNotificationPublisher(ILogger<NoopNotificationPublisher> logger) => _logger = logger;

    public Task PublishAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Notification ({Type}) to {User}: {Title}",
            message.Type, message.UserId?.ToString() ?? "all", message.Title);
        return Task.CompletedTask;
    }
}
