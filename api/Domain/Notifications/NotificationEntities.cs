namespace Wintech.Hrms.Api.Domain.Notifications;

/// <summary>
/// In-app notification inbox row (FR-EMP-120/122). Notifications are created server-side by
/// domain events through <c>INotificationPublisher</c>; sensitive payloads never carry detail
/// in the body (BR-013) — the deep link leads to the authoritative record.
/// </summary>
public class AppNotification
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? EmployeeId { get; set; }
    /// <summary>Machine type, e.g. "CaseUpdated", "AnnouncementPublished".</summary>
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? DeepLink { get; set; }
    /// <summary>Notification category, e.g. "Case", "Announcement", "Leave", "Payroll".</summary>
    public string Category { get; set; } = "System";
    /// <summary>True when the payload must not be exposed on a lock screen (BR-013).</summary>
    public bool Sensitive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
}
