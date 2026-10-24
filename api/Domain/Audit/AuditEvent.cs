namespace Wintech.Hrms.Api.Domain.Audit;

/// <summary>
/// Append-only audit trail (FR-ADM-152/153). Rows are created through IAuditService only and are
/// never updated or deleted by application code.
/// </summary>
public class AuditEvent
{
    public Guid Id { get; set; }
    public DateTime Timestamp { get; set; }
    public Guid? ActorUserId { get; set; }
    public string? ActorEmployeeNumber { get; set; }
    public string ActorName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string Outcome { get; set; } = "Success";
    /// <summary>JSON details — must never contain passwords, tokens, full bank details or document contents.</summary>
    public string? Details { get; set; }
    public string? CorrelationId { get; set; }
    public string? IpAddress { get; set; }
}
