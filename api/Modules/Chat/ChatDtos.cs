using System.ComponentModel.DataAnnotations;

namespace Wintech.Hrms.Api.Modules.Chat;

// ---------------------------------------------------------------- read models

public sealed class ChatContactDto
{
    public Guid EmployeeId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string EmployeeNumber { get; set; } = string.Empty;
    public string? Department { get; set; }
    public string? Designation { get; set; }
    public string? AvatarUrl { get; set; }
}

public sealed class ConversationMemberDto
{
    public Guid EmployeeId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
}

public sealed class ConversationListItemDto
{
    public Guid Id { get; set; }
    public string Type { get; set; } = "Direct";
    public string Title { get; set; } = string.Empty;
    public string? LastMessagePreview { get; set; }
    public DateTime LastMessageAt { get; set; }
    public int UnreadCount { get; set; }
    public int MemberCount { get; set; }
    /// <summary>For direct conversations, the other participant's employee id.</summary>
    public Guid? OtherEmployeeId { get; set; }
}

public sealed class ChatMessageDto
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Guid SenderEmployeeId { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public string Kind { get; set; } = "Text";
    public string? Body { get; set; }
    public string? AttachmentUrl { get; set; }
    public string? AttachmentFileName { get; set; }
    public string? AttachmentContentType { get; set; }
    public long? AttachmentSize { get; set; }
    public int? DurationSeconds { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool Mine { get; set; }
}

public sealed class ConversationDetailDto
{
    public Guid Id { get; set; }
    public string Type { get; set; } = "Direct";
    public string Title { get; set; } = string.Empty;
    public IReadOnlyList<ConversationMemberDto> Members { get; set; } = [];
    public IReadOnlyList<ChatMessageDto> Messages { get; set; } = [];
}

public sealed class CallHistoryItemDto
{
    public Guid Id { get; set; }
    public Guid OtherEmployeeId { get; set; }
    public string OtherName { get; set; } = string.Empty;
    public string Kind { get; set; } = "Audio";
    public string State { get; set; } = "Ended";
    public bool Outgoing { get; set; }
    public DateTime StartedAt { get; set; }
    public int? DurationSeconds { get; set; }
}

// ---------------------------------------------------------------- write models

public sealed class StartDirectRequest
{
    [Required]
    public Guid EmployeeId { get; set; }
}

public sealed class CreateGroupRequest
{
    [Required, StringLength(200, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [Required, MinLength(1)]
    public List<Guid> MemberIds { get; set; } = [];
}

public sealed class SendMessageRequest
{
    [StringLength(4000)]
    public string? Body { get; set; }
}

public sealed class UpdateGroupMembersRequest
{
    [Required]
    public List<Guid> MemberIds { get; set; } = [];
}

public sealed class StartCallRequest
{
    [Required]
    public Guid CalleeEmployeeId { get; set; }

    /// <summary>"Audio" or "Video".</summary>
    [Required]
    public string Kind { get; set; } = "Audio";
}

public sealed class EndCallRequest
{
    /// <summary>Final state: Accepted->Ended, or Declined/Missed/Cancelled.</summary>
    [Required]
    public string State { get; set; } = "Ended";

    public int? DurationSeconds { get; set; }
}

public sealed class RegisterDeviceRequest
{
    [Required, StringLength(400)]
    public string Token { get; set; } = string.Empty;

    [StringLength(20)]
    public string? Platform { get; set; }
}
