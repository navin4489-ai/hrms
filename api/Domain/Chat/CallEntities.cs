using Wintech.Hrms.Api.Common.Tenancy;

namespace Wintech.Hrms.Api.Domain.Chat;

public enum CallKind
{
    Audio = 0,
    Video = 1
}

public enum CallState
{
    Ringing = 0,
    Accepted = 1,
    Declined = 2,
    Missed = 3,
    Ended = 4,
    Cancelled = 5
}

/// <summary>
/// A voice/video call session between two employees in the same organization. WebRTC media is
/// peer-to-peer; this row records call metadata and drives the call history and missed-call list.
/// Signaling (offer/answer/ICE) flows over SignalR and is not persisted.
/// </summary>
public class CallSession : IOrgOwned
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }

    public Guid CallerEmployeeId { get; set; }
    public Guid CalleeEmployeeId { get; set; }
    public CallKind Kind { get; set; }
    public CallState State { get; set; }

    public DateTime StartedAt { get; set; }
    public DateTime? AnsweredAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public int? DurationSeconds { get; set; }
}
