using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.CurrentUser;
using Wintech.Hrms.Api.Common.ErrorHandling;
using Wintech.Hrms.Api.Common.Files;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Chat;
using Wintech.Hrms.Api.Domain.Employees;

namespace Wintech.Hrms.Api.Modules.Chat;

public sealed class ChatService : IChatService
{
    private const long MaxAttachmentBytes = 25 * 1024 * 1024;
    private static readonly string[] FileExtensions =
        [".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".csv", ".zip",
         ".png", ".jpg", ".jpeg", ".gif", ".webp", ".heic", ".m4a", ".mp3", ".aac", ".ogg", ".wav", ".webm"];

    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IPrivateFileStore _files;
    private readonly IHubContext<ChatHub> _hub;
    private readonly ILogger<ChatService> _logger;

    public ChatService(AppDbContext db, ICurrentUser currentUser, IClock clock, IPrivateFileStore files,
        IHubContext<ChatHub> hub, ILogger<ChatService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _files = files;
        _hub = hub;
        _logger = logger;
    }

    private Guid MeEmployeeId => _currentUser.EmployeeId
        ?? throw new ForbiddenException("Chat is only available to employees.");

    // ---------------------------------------------------------------- contacts

    public async Task<IReadOnlyList<ChatContactDto>> GetContactsAsync(string? search, CancellationToken cancellationToken = default)
    {
        var me = MeEmployeeId;
        var query = _db.Employees.AsNoTracking()
            .Where(e => e.Id != me && e.EmploymentStatus != EmploymentStatus.Terminated);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(e => e.DisplayName.ToLower().Contains(term) ||
                                     e.EmployeeNumber.ToLower().Contains(term));
        }

        return await query
            .OrderBy(e => e.DisplayName)
            .Take(100)
            .Select(e => new ChatContactDto
            {
                EmployeeId = e.Id,
                DisplayName = e.DisplayName,
                EmployeeNumber = e.EmployeeNumber,
                Department = e.Department != null ? e.Department.Name : null,
                Designation = e.Designation != null ? e.Designation.Name : null
            })
            .ToListAsync(cancellationToken);
    }

    // ---------------------------------------------------------------- conversations

    public async Task<IReadOnlyList<ConversationListItemDto>> GetConversationsAsync(CancellationToken cancellationToken = default)
    {
        var me = MeEmployeeId;
        var memberships = await _db.Set<ConversationMember>().AsNoTracking()
            .Where(m => m.EmployeeId == me && m.LeftAt == null)
            .Select(m => new { m.ConversationId, m.LastReadAt })
            .ToListAsync(cancellationToken);

        var ids = memberships.Select(m => m.ConversationId).ToList();
        if (ids.Count == 0) return [];

        var conversations = await _db.Set<Conversation>().AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Include(c => c.Members)
            .OrderByDescending(c => c.LastMessageAt)
            .ToListAsync(cancellationToken);

        var otherIds = conversations
            .Where(c => c.Type == ConversationType.Direct)
            .SelectMany(c => c.Members.Select(m => m.EmployeeId))
            .Where(id => id != me)
            .Distinct()
            .ToList();
        var names = await NamesAsync(otherIds, cancellationToken);

        var readMap = memberships.ToDictionary(m => m.ConversationId, m => m.LastReadAt);

        var result = new List<ConversationListItemDto>();
        foreach (var c in conversations)
        {
            var lastRead = readMap.GetValueOrDefault(c.Id);
            var unread = await _db.Set<ChatMessage>().AsNoTracking()
                .CountAsync(m => m.ConversationId == c.Id && !m.Deleted && m.SenderEmployeeId != me &&
                                 (lastRead == null || m.CreatedAt > lastRead), cancellationToken);

            Guid? otherId = null;
            string title;
            if (c.Type == ConversationType.Direct)
            {
                otherId = c.Members.Select(m => m.EmployeeId).FirstOrDefault(id => id != me);
                title = otherId is { } o ? names.GetValueOrDefault(o, "Unknown") : "Direct chat";
            }
            else
            {
                title = c.Name ?? "Group";
            }

            result.Add(new ConversationListItemDto
            {
                Id = c.Id,
                Type = c.Type.ToString(),
                Title = title,
                LastMessagePreview = c.LastMessagePreview,
                LastMessageAt = c.LastMessageAt,
                UnreadCount = unread,
                MemberCount = c.Members.Count(m => m.LeftAt == null),
                OtherEmployeeId = otherId
            });
        }
        return result;
    }

    public async Task<ConversationDetailDto> GetConversationAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        var me = MeEmployeeId;
        var conversation = await LoadMemberConversationAsync(conversationId, me, cancellationToken);

        var memberIds = conversation.Members.Where(m => m.LeftAt == null).Select(m => m.EmployeeId).ToList();
        var names = await NamesAsync(memberIds, cancellationToken);

        var messages = await _db.Set<ChatMessage>().AsNoTracking()
            .Where(m => m.ConversationId == conversationId && !m.Deleted)
            .OrderByDescending(m => m.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);
        messages.Reverse();

        var senderNames = await NamesAsync(messages.Select(m => m.SenderEmployeeId).Distinct().ToList(), cancellationToken);

        return new ConversationDetailDto
        {
            Id = conversation.Id,
            Type = conversation.Type.ToString(),
            Title = conversation.Type == ConversationType.Direct
                ? names.GetValueOrDefault(memberIds.FirstOrDefault(id => id != me), "Direct chat")
                : conversation.Name ?? "Group",
            Members = conversation.Members.Where(m => m.LeftAt == null).Select(m => new ConversationMemberDto
            {
                EmployeeId = m.EmployeeId,
                DisplayName = names.GetValueOrDefault(m.EmployeeId, "Unknown"),
                IsAdmin = m.IsAdmin
            }).ToList(),
            Messages = messages.Select(m => ToDto(m, senderNames, me)).ToList()
        };
    }

    public async Task<ConversationListItemDto> StartDirectAsync(Guid otherEmployeeId, CancellationToken cancellationToken = default)
    {
        var me = MeEmployeeId;
        if (otherEmployeeId == me) throw new RequestValidationException("You cannot start a chat with yourself.");

        var other = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == otherEmployeeId, cancellationToken)
            ?? throw new NotFoundException("Employee not found in your organization.");

        var directKey = DirectKey(me, otherEmployeeId);
        var existing = await _db.Set<Conversation>()
            .Include(c => c.Members)
            .FirstOrDefaultAsync(c => c.DirectKey == directKey, cancellationToken);

        if (existing is not null)
            return await ToListItemAsync(existing, me, cancellationToken);

        var now = _clock.UtcNow;
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            Type = ConversationType.Direct,
            DirectKey = directKey,
            CreatedByEmployeeId = me,
            CreatedAt = now,
            LastMessageAt = now
        };
        conversation.Members.Add(NewMember(conversation.Id, me, isAdmin: true, now));
        conversation.Members.Add(NewMember(conversation.Id, otherEmployeeId, isAdmin: false, now));
        _db.Add(conversation);
        await _db.SaveChangesAsync(cancellationToken);

        await NotifyConversationUpdatedAsync(conversation, cancellationToken);
        _ = other;
        return await ToListItemAsync(conversation, me, cancellationToken);
    }

    public async Task<ConversationListItemDto> CreateGroupAsync(string name, IReadOnlyList<Guid> memberIds, CancellationToken cancellationToken = default)
    {
        var me = MeEmployeeId;
        var distinct = memberIds.Where(id => id != me).Distinct().ToList();

        var valid = await _db.Employees.AsNoTracking()
            .Where(e => distinct.Contains(e.Id))
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);
        if (valid.Count == 0)
            throw new RequestValidationException("Add at least one colleague to the group.");

        var now = _clock.UtcNow;
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            Type = ConversationType.Group,
            Name = name.Trim(),
            CreatedByEmployeeId = me,
            CreatedAt = now,
            LastMessageAt = now,
            LastMessagePreview = "Group created"
        };
        conversation.Members.Add(NewMember(conversation.Id, me, isAdmin: true, now));
        foreach (var id in valid)
            conversation.Members.Add(NewMember(conversation.Id, id, isAdmin: false, now));
        _db.Add(conversation);
        await _db.SaveChangesAsync(cancellationToken);

        await NotifyConversationUpdatedAsync(conversation, cancellationToken);
        return await ToListItemAsync(conversation, me, cancellationToken);
    }

    public async Task UpdateGroupMembersAsync(Guid conversationId, IReadOnlyList<Guid> memberIds, CancellationToken cancellationToken = default)
    {
        var me = MeEmployeeId;
        var conversation = await LoadMemberConversationAsync(conversationId, me, cancellationToken);
        if (conversation.Type != ConversationType.Group)
            throw new RequestValidationException("Only groups can change members.");
        if (!conversation.Members.Any(m => m.EmployeeId == me && m.IsAdmin && m.LeftAt == null))
            throw new ForbiddenException("Only a group admin can change members.");

        var now = _clock.UtcNow;
        var desired = memberIds.Distinct().ToHashSet();
        desired.Add(me); // admin always stays

        var valid = await _db.Employees.AsNoTracking()
            .Where(e => desired.Contains(e.Id)).Select(e => e.Id).ToListAsync(cancellationToken);
        var validSet = valid.ToHashSet();

        foreach (var member in conversation.Members)
        {
            var shouldBeActive = validSet.Contains(member.EmployeeId);
            if (shouldBeActive && member.LeftAt != null) { member.LeftAt = null; member.JoinedAt = now; }
            else if (!shouldBeActive && member.LeftAt == null && member.EmployeeId != me) member.LeftAt = now;
        }
        var existingIds = conversation.Members.Select(m => m.EmployeeId).ToHashSet();
        foreach (var id in validSet.Where(id => !existingIds.Contains(id)))
            conversation.Members.Add(NewMember(conversation.Id, id, isAdmin: false, now));

        await _db.SaveChangesAsync(cancellationToken);
        await NotifyConversationUpdatedAsync(conversation, cancellationToken);
    }

    public async Task LeaveConversationAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        var me = MeEmployeeId;
        var conversation = await LoadMemberConversationAsync(conversationId, me, cancellationToken);
        var member = conversation.Members.First(m => m.EmployeeId == me && m.LeftAt == null);
        member.LeftAt = _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    // ---------------------------------------------------------------- messages

    public async Task<ChatMessageDto> SendTextAsync(Guid conversationId, string body, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(body)) throw new RequestValidationException("Message body is required.");
        var me = MeEmployeeId;
        var conversation = await LoadMemberConversationAsync(conversationId, me, cancellationToken);

        var message = NewMessage(conversation, me, ChatMessageKind.Text, body.Trim());
        _db.Add(message);
        Touch(conversation, Preview(body.Trim()));
        await _db.SaveChangesAsync(cancellationToken);

        var dto = await FinalizeAndBroadcastAsync(conversation, message, me, cancellationToken);
        return dto;
    }

    public async Task<ChatMessageDto> SendAttachmentAsync(Guid conversationId, Stream content, string fileName,
        ChatAttachmentKind kind, int? durationSeconds, CancellationToken cancellationToken = default)
    {
        var me = MeEmployeeId;
        var conversation = await LoadMemberConversationAsync(conversationId, me, cancellationToken);

        var stored = await _files.SaveAsync(content, fileName, MaxAttachmentBytes, FileExtensions, cancellationToken);

        var msgKind = kind switch
        {
            ChatAttachmentKind.Image => ChatMessageKind.Image,
            ChatAttachmentKind.VoiceNote => ChatMessageKind.VoiceNote,
            _ => ChatMessageKind.File
        };
        var message = NewMessage(conversation, me, msgKind, null);
        message.AttachmentStorageKey = stored.StorageKey;
        message.AttachmentFileName = stored.OriginalFileName;
        message.AttachmentContentType = stored.ContentType;
        message.AttachmentSize = stored.SizeBytes;
        message.DurationSeconds = durationSeconds;
        _db.Add(message);

        var preview = msgKind switch
        {
            ChatMessageKind.Image => "📷 Photo",
            ChatMessageKind.VoiceNote => "🎤 Voice note",
            _ => "📎 " + stored.OriginalFileName
        };
        Touch(conversation, preview);
        await _db.SaveChangesAsync(cancellationToken);

        return await FinalizeAndBroadcastAsync(conversation, message, me, cancellationToken);
    }

    public async Task MarkReadAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        var me = MeEmployeeId;
        var member = await _db.Set<ConversationMember>()
            .FirstOrDefaultAsync(m => m.ConversationId == conversationId && m.EmployeeId == me, cancellationToken)
            ?? throw new NotFoundException("Conversation not found.");
        member.LastReadAt = _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<AttachmentDownload> OpenAttachmentAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var me = MeEmployeeId;
        var message = await _db.Set<ChatMessage>().AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == messageId && !m.Deleted, cancellationToken)
            ?? throw new NotFoundException("Message not found.");
        if (string.IsNullOrEmpty(message.AttachmentStorageKey))
            throw new NotFoundException("This message has no attachment.");

        // Caller must be an active member of the conversation.
        var isMember = await _db.Set<ConversationMember>().AsNoTracking()
            .AnyAsync(m => m.ConversationId == message.ConversationId && m.EmployeeId == me, cancellationToken);
        if (!isMember) throw new ForbiddenException("You are not a member of this conversation.");

        var file = await _files.OpenReadAsync(message.AttachmentStorageKey, cancellationToken);
        return new AttachmentDownload(file.Content, file.ContentType,
            message.AttachmentFileName ?? file.OriginalFileName);
    }

    public async Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default)
    {
        var conversations = await GetConversationsAsync(cancellationToken);
        return conversations.Sum(c => c.UnreadCount);
    }

    // ---------------------------------------------------------------- calling

    public async Task<CallStartedDto> StartCallAsync(Guid calleeEmployeeId, string kind, CancellationToken cancellationToken = default)
    {
        var me = MeEmployeeId;
        if (calleeEmployeeId == me) throw new RequestValidationException("You cannot call yourself.");
        var callee = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == calleeEmployeeId, cancellationToken)
            ?? throw new NotFoundException("Employee not found in your organization.");

        var callKind = kind.Equals("Video", StringComparison.OrdinalIgnoreCase) ? CallKind.Video : CallKind.Audio;
        var call = new CallSession
        {
            Id = Guid.NewGuid(),
            CallerEmployeeId = me,
            CalleeEmployeeId = calleeEmployeeId,
            Kind = callKind,
            State = CallState.Ringing,
            StartedAt = _clock.UtcNow
        };
        _db.Add(call);
        await _db.SaveChangesAsync(cancellationToken);

        var callerName = (await NamesAsync([me], cancellationToken)).GetValueOrDefault(me, "Caller");
        await SendToEmployeeAsync(calleeEmployeeId, "incomingCall", new
        {
            callId = call.Id,
            fromEmployeeId = me,
            fromName = callerName,
            kind = callKind.ToString()
        });
        _ = callee;
        return new CallStartedDto { CallId = call.Id, CalleeEmployeeId = calleeEmployeeId, Kind = callKind.ToString() };
    }

    public async Task EndCallAsync(Guid callId, string state, int? durationSeconds, CancellationToken cancellationToken = default)
    {
        var me = MeEmployeeId;
        var call = await _db.Set<CallSession>().FirstOrDefaultAsync(c => c.Id == callId, cancellationToken)
            ?? throw new NotFoundException("Call not found.");
        if (call.CallerEmployeeId != me && call.CalleeEmployeeId != me)
            throw new ForbiddenException("You are not part of this call.");

        if (!Enum.TryParse<CallState>(state, ignoreCase: true, out var newState))
            newState = CallState.Ended;

        call.State = newState;
        call.EndedAt = _clock.UtcNow;
        if (newState == CallState.Ended)
        {
            call.AnsweredAt ??= call.StartedAt;
            call.DurationSeconds = durationSeconds ?? (int)(call.EndedAt.Value - (call.AnsweredAt ?? call.StartedAt)).TotalSeconds;
        }
        await _db.SaveChangesAsync(cancellationToken);

        var other = call.CallerEmployeeId == me ? call.CalleeEmployeeId : call.CallerEmployeeId;
        await SendToEmployeeAsync(other, "callEnded", new { callId = call.Id, state = newState.ToString() });
    }

    public async Task<IReadOnlyList<CallHistoryItemDto>> GetCallHistoryAsync(CancellationToken cancellationToken = default)
    {
        var me = MeEmployeeId;
        var calls = await _db.Set<CallSession>().AsNoTracking()
            .Where(c => c.CallerEmployeeId == me || c.CalleeEmployeeId == me)
            .OrderByDescending(c => c.StartedAt)
            .Take(100)
            .ToListAsync(cancellationToken);

        var otherIds = calls.Select(c => c.CallerEmployeeId == me ? c.CalleeEmployeeId : c.CallerEmployeeId)
            .Distinct().ToList();
        var names = await NamesAsync(otherIds, cancellationToken);

        return calls.Select(c =>
        {
            var outgoing = c.CallerEmployeeId == me;
            var otherId = outgoing ? c.CalleeEmployeeId : c.CallerEmployeeId;
            return new CallHistoryItemDto
            {
                Id = c.Id,
                OtherEmployeeId = otherId,
                OtherName = names.GetValueOrDefault(otherId, "Unknown"),
                Kind = c.Kind.ToString(),
                State = c.State.ToString(),
                Outgoing = outgoing,
                StartedAt = c.StartedAt,
                DurationSeconds = c.DurationSeconds
            };
        }).ToList();
    }

    // ---------------------------------------------------------------- helpers

    private async Task<Conversation> LoadMemberConversationAsync(Guid conversationId, Guid me, CancellationToken ct)
    {
        var conversation = await _db.Set<Conversation>()
            .Include(c => c.Members)
            .FirstOrDefaultAsync(c => c.Id == conversationId, ct)
            ?? throw new NotFoundException("Conversation not found.");
        if (!conversation.Members.Any(m => m.EmployeeId == me && m.LeftAt == null))
            throw new ForbiddenException("You are not a member of this conversation.");
        return conversation;
    }

    private ConversationMember NewMember(Guid conversationId, Guid employeeId, bool isAdmin, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        ConversationId = conversationId,
        EmployeeId = employeeId,
        IsAdmin = isAdmin,
        JoinedAt = now,
        LastReadAt = isAdmin ? now : null
    };

    private ChatMessage NewMessage(Conversation conversation, Guid sender, ChatMessageKind kind, string? body) => new()
    {
        Id = Guid.NewGuid(),
        ConversationId = conversation.Id,
        SenderEmployeeId = sender,
        Kind = kind,
        Body = body,
        CreatedAt = _clock.UtcNow
    };

    private void Touch(Conversation conversation, string preview)
    {
        conversation.LastMessageAt = _clock.UtcNow;
        conversation.LastMessagePreview = preview;
    }

    private async Task<ChatMessageDto> FinalizeAndBroadcastAsync(Conversation conversation, ChatMessage message, Guid me, CancellationToken ct)
    {
        var names = await NamesAsync([me], ct);
        var dto = ToDto(message, names, me);
        // sender marks own conversation read
        var myMember = conversation.Members.FirstOrDefault(m => m.EmployeeId == me);
        if (myMember is not null) { myMember.LastReadAt = _clock.UtcNow; await _db.SaveChangesAsync(ct); }

        foreach (var member in conversation.Members.Where(m => m.LeftAt == null && m.EmployeeId != me))
            await SendToEmployeeAsync(member.EmployeeId, "message", dto);
        return dto;
    }

    private async Task NotifyConversationUpdatedAsync(Conversation conversation, CancellationToken ct)
    {
        foreach (var member in conversation.Members.Where(m => m.LeftAt == null))
            await SendToEmployeeAsync(member.EmployeeId, "conversationUpdated", new { conversationId = conversation.Id });
    }

    private Task SendToEmployeeAsync(Guid employeeId, string method, object payload)
    {
        var connections = ChatHub.ConnectionsFor(employeeId.ToString());
        return connections.Count > 0
            ? _hub.Clients.Clients(connections).SendAsync(method, payload)
            : Task.CompletedTask;
    }

    private async Task<Dictionary<Guid, string>> NamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        return await _db.Employees.AsNoTracking()
            .Where(e => ids.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.DisplayName, ct);
    }

    private async Task<ConversationListItemDto> ToListItemAsync(Conversation conversation, Guid me, CancellationToken ct)
    {
        Guid? otherId = null;
        string title;
        if (conversation.Type == ConversationType.Direct)
        {
            otherId = conversation.Members.Select(m => m.EmployeeId).FirstOrDefault(id => id != me);
            var names = await NamesAsync(otherId is { } o ? [o] : [], ct);
            title = otherId is { } oo ? names.GetValueOrDefault(oo, "Direct chat") : "Direct chat";
        }
        else
        {
            title = conversation.Name ?? "Group";
        }
        return new ConversationListItemDto
        {
            Id = conversation.Id,
            Type = conversation.Type.ToString(),
            Title = title,
            LastMessagePreview = conversation.LastMessagePreview,
            LastMessageAt = conversation.LastMessageAt,
            UnreadCount = 0,
            MemberCount = conversation.Members.Count(m => m.LeftAt == null),
            OtherEmployeeId = otherId
        };
    }

    private static ChatMessageDto ToDto(ChatMessage m, IReadOnlyDictionary<Guid, string> names, Guid me) => new()
    {
        Id = m.Id,
        ConversationId = m.ConversationId,
        SenderEmployeeId = m.SenderEmployeeId,
        SenderName = names.GetValueOrDefault(m.SenderEmployeeId, "Unknown"),
        Kind = m.Kind.ToString(),
        Body = m.Body,
        AttachmentUrl = m.AttachmentStorageKey != null ? $"/api/v1/chat/messages/{m.Id}/attachment" : null,
        AttachmentFileName = m.AttachmentFileName,
        AttachmentContentType = m.AttachmentContentType,
        AttachmentSize = m.AttachmentSize,
        DurationSeconds = m.DurationSeconds,
        CreatedAt = m.CreatedAt,
        Mine = m.SenderEmployeeId == me
    };

    private static string DirectKey(Guid a, Guid b)
    {
        var (min, max) = a.CompareTo(b) <= 0 ? (a, b) : (b, a);
        return $"{min}:{max}";
    }

    private static string Preview(string body) => body.Length <= 120 ? body : body[..120];
}
