using Wintech.Hrms.Api.Domain.Notifications;

namespace Wintech.Hrms.Api.Modules.Notifications;

public sealed class NotificationDto
{
    public Guid Id { get; init; }
    public string Type { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string? DeepLink { get; init; }
    public string Category { get; init; } = "System";
    public DateTime CreatedAt { get; init; }
    public DateTime? ReadAt { get; init; }
}

public sealed record UnreadCountResult(int Count);
