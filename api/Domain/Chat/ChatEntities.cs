using Wintech.Hrms.Api.Common.Tenancy;

namespace Wintech.Hrms.Api.Domain.Chat;

public enum ConversationType
{
    Direct = 0,
    Group = 1
}

public enum ChatMessageKind
{
    Text = 0,
    File = 1,
    Image = 2,
    VoiceNote = 3,
    System = 4
}

/// <summary>
/// A chat conversation (1:1 or group) scoped to a single organization. Direct conversations have
/// exactly two members; groups have a name and an owner. Org-owned for tenant isolation.
/// </summary>
public class Conversation : IOrgOwned
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public ConversationType Type { get; set; }

    /// <summary>Group title (null for direct conversations).</summary>
    public string? Name { get; set; }

    /// <summary>Stable key for direct conversations ("{minEmpId}:{maxEmpId}") to prevent duplicates.</summary>
    public string? DirectKey { get; set; }

    public Guid CreatedByEmployeeId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastMessageAt { get; set; }
    public string? LastMessagePreview { get; set; }

    public ICollection<ConversationMember> Members { get; set; } = [];
    public ICollection<ChatMessage> Messages { get; set; } = [];
}

/// <summary>Membership of an employee in a conversation, with per-member read state.</summary>
public class ConversationMember : IOrgOwned
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ConversationId { get; set; }
    public Conversation? Conversation { get; set; }
    public Guid EmployeeId { get; set; }

    public bool IsAdmin { get; set; }
    public DateTime JoinedAt { get; set; }
    public DateTime? LeftAt { get; set; }

    /// <summary>Timestamp of the last message this member has read; drives unread counts.</summary>
    public DateTime? LastReadAt { get; set; }
}

/// <summary>A single message. File/voice payloads are stored in the private file store.</summary>
public class ChatMessage : IOrgOwned
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ConversationId { get; set; }
    public Conversation? Conversation { get; set; }
    public Guid SenderEmployeeId { get; set; }

    public ChatMessageKind Kind { get; set; }
    public string? Body { get; set; }

    /// <summary>Private file-store key for file/image/voice messages.</summary>
    public string? AttachmentStorageKey { get; set; }
    public string? AttachmentFileName { get; set; }
    public string? AttachmentContentType { get; set; }
    public long? AttachmentSize { get; set; }
    /// <summary>Voice-note duration in seconds.</summary>
    public int? DurationSeconds { get; set; }

    public DateTime CreatedAt { get; set; }
    public bool Deleted { get; set; }
}
