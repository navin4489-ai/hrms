using Wintech.Hrms.Api.Common.Registration;

namespace Wintech.Hrms.Api.Modules.Chat;

/// <summary>
/// Employee chat (direct + group) and calling within an organization. All operations are tenant-scoped
/// via the global query filter and additionally verify the caller is a member of the conversation.
/// </summary>
public interface IChatService : IAppService
{
    Task<IReadOnlyList<ChatContactDto>> GetContactsAsync(string? search, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ConversationListItemDto>> GetConversationsAsync(CancellationToken cancellationToken = default);
    Task<ConversationDetailDto> GetConversationAsync(Guid conversationId, CancellationToken cancellationToken = default);

    Task<ConversationListItemDto> StartDirectAsync(Guid otherEmployeeId, CancellationToken cancellationToken = default);
    Task<ConversationListItemDto> CreateGroupAsync(string name, IReadOnlyList<Guid> memberIds, CancellationToken cancellationToken = default);
    Task UpdateGroupMembersAsync(Guid conversationId, IReadOnlyList<Guid> memberIds, CancellationToken cancellationToken = default);
    Task LeaveConversationAsync(Guid conversationId, CancellationToken cancellationToken = default);

    Task<ChatMessageDto> SendTextAsync(Guid conversationId, string body, CancellationToken cancellationToken = default);
    Task<ChatMessageDto> SendAttachmentAsync(Guid conversationId, Stream content, string fileName,
        ChatAttachmentKind kind, int? durationSeconds, CancellationToken cancellationToken = default);
    Task MarkReadAsync(Guid conversationId, CancellationToken cancellationToken = default);
    Task<AttachmentDownload> OpenAttachmentAsync(Guid messageId, CancellationToken cancellationToken = default);

    Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default);

    // ---- calling ----
    Task<CallStartedDto> StartCallAsync(Guid calleeEmployeeId, string kind, CancellationToken cancellationToken = default);
    Task EndCallAsync(Guid callId, string state, int? durationSeconds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CallHistoryItemDto>> GetCallHistoryAsync(CancellationToken cancellationToken = default);
}

public enum ChatAttachmentKind
{
    File,
    Image,
    VoiceNote
}

public sealed record AttachmentDownload(Stream Content, string ContentType, string FileName);

public sealed class CallStartedDto
{
    public Guid CallId { get; set; }
    public Guid CalleeEmployeeId { get; set; }
    public string Kind { get; set; } = "Audio";
}
