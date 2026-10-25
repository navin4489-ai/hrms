using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wintech.Hrms.Api.Common.Tenancy;
using Wintech.Hrms.Api.Modules.Organizations;
using Wintech.Hrms.Api.Security;
using Wintech.Hrms.Api.Security.Auth;

namespace Wintech.Hrms.Api.Modules.Chat;

/// <summary>
/// Employee chat and calling within an organization (module-gated). Requires the Chat/Calling module
/// to be enabled for the caller's org and the <c>chat.use</c>/<c>call.use</c> permission.
/// </summary>
[ApiController]
[Route("api/v1/chat")]
[Authorize]
[Produces("application/json")]
public sealed class ChatController : ControllerBase
{
    private readonly IChatService _chat;
    private readonly IOrganizationService _organizations;

    public ChatController(IChatService chat, IOrganizationService organizations)
    {
        _chat = chat;
        _organizations = organizations;
    }

    private async Task EnsureModuleAsync(string moduleKey, CancellationToken ct)
    {
        var modules = await _organizations.GetMyModulesAsync(ct);
        var enabled = modules.Any(m => m.Key == moduleKey && m.Enabled);
        if (!enabled)
            throw new Common.ErrorHandling.ForbiddenException(
                $"The {moduleKey} feature is not enabled for your organization.");
    }

    // ---- contacts & conversations ----

    [HttpGet("contacts")]
    [Permission(Permissions.ChatUse)]
    public async Task<ActionResult<IReadOnlyList<ChatContactDto>>> Contacts(
        [FromQuery] string? search, CancellationToken cancellationToken)
    {
        await EnsureModuleAsync(OrgModules.Chat, cancellationToken);
        return Ok(await _chat.GetContactsAsync(search, cancellationToken));
    }

    [HttpGet("conversations")]
    [Permission(Permissions.ChatUse)]
    public async Task<ActionResult<IReadOnlyList<ConversationListItemDto>>> Conversations(CancellationToken cancellationToken)
    {
        await EnsureModuleAsync(OrgModules.Chat, cancellationToken);
        return Ok(await _chat.GetConversationsAsync(cancellationToken));
    }

    [HttpGet("conversations/{id:guid}")]
    [Permission(Permissions.ChatUse)]
    public async Task<ActionResult<ConversationDetailDto>> Conversation(Guid id, CancellationToken cancellationToken)
    {
        await EnsureModuleAsync(OrgModules.Chat, cancellationToken);
        return Ok(await _chat.GetConversationAsync(id, cancellationToken));
    }

    [HttpPost("conversations/direct")]
    [Permission(Permissions.ChatUse)]
    public async Task<ActionResult<ConversationListItemDto>> StartDirect(
        [FromBody] StartDirectRequest request, CancellationToken cancellationToken)
    {
        await EnsureModuleAsync(OrgModules.Chat, cancellationToken);
        return Ok(await _chat.StartDirectAsync(request.EmployeeId, cancellationToken));
    }

    [HttpPost("conversations/group")]
    [Permission(Permissions.ChatUse)]
    public async Task<ActionResult<ConversationListItemDto>> CreateGroup(
        [FromBody] CreateGroupRequest request, CancellationToken cancellationToken)
    {
        await EnsureModuleAsync(OrgModules.Chat, cancellationToken);
        return StatusCode(StatusCodes.Status201Created,
            await _chat.CreateGroupAsync(request.Name, request.MemberIds, cancellationToken));
    }

    [HttpPut("conversations/{id:guid}/members")]
    [Permission(Permissions.ChatUse)]
    public async Task<IActionResult> UpdateMembers(Guid id,
        [FromBody] UpdateGroupMembersRequest request, CancellationToken cancellationToken)
    {
        await EnsureModuleAsync(OrgModules.Chat, cancellationToken);
        await _chat.UpdateGroupMembersAsync(id, request.MemberIds, cancellationToken);
        return NoContent();
    }

    [HttpPost("conversations/{id:guid}/leave")]
    [Permission(Permissions.ChatUse)]
    public async Task<IActionResult> Leave(Guid id, CancellationToken cancellationToken)
    {
        await EnsureModuleAsync(OrgModules.Chat, cancellationToken);
        await _chat.LeaveConversationAsync(id, cancellationToken);
        return NoContent();
    }

    // ---- messages ----

    [HttpPost("conversations/{id:guid}/messages")]
    [Permission(Permissions.ChatUse)]
    public async Task<ActionResult<ChatMessageDto>> SendText(Guid id,
        [FromBody] SendMessageRequest request, CancellationToken cancellationToken)
    {
        await EnsureModuleAsync(OrgModules.Chat, cancellationToken);
        return StatusCode(StatusCodes.Status201Created,
            await _chat.SendTextAsync(id, request.Body ?? string.Empty, cancellationToken));
    }

    [HttpPost("conversations/{id:guid}/attachments")]
    [Permission(Permissions.ChatUse)]
    [RequestSizeLimit(26 * 1024 * 1024)]
    public async Task<ActionResult<ChatMessageDto>> SendAttachment(Guid id,
        [FromForm] IFormFile file, [FromForm] string? kind, [FromForm] int? durationSeconds,
        CancellationToken cancellationToken)
    {
        await EnsureModuleAsync(OrgModules.Chat, cancellationToken);
        if (file is null || file.Length == 0)
            return BadRequest(new { title = "A file is required." });

        var attachmentKind = (kind ?? string.Empty).ToLowerInvariant() switch
        {
            "image" => ChatAttachmentKind.Image,
            "voice" or "voicenote" => ChatAttachmentKind.VoiceNote,
            _ => ChatAttachmentKind.File
        };
        await using var stream = file.OpenReadStream();
        var dto = await _chat.SendAttachmentAsync(id, stream, file.FileName, attachmentKind, durationSeconds, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, dto);
    }

    [HttpGet("messages/{id:guid}/attachment")]
    [Permission(Permissions.ChatUse)]
    public async Task<IActionResult> Attachment(Guid id, CancellationToken cancellationToken)
    {
        await EnsureModuleAsync(OrgModules.Chat, cancellationToken);
        var file = await _chat.OpenAttachmentAsync(id, cancellationToken);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpPost("conversations/{id:guid}/read")]
    [Permission(Permissions.ChatUse)]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken)
    {
        await _chat.MarkReadAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpGet("unread-count")]
    [Permission(Permissions.ChatUse)]
    public async Task<ActionResult<object>> Unread(CancellationToken cancellationToken)
        => Ok(new { count = await _chat.GetUnreadCountAsync(cancellationToken) });

    // ---- calling ----

    [HttpPost("calls")]
    [Permission(Permissions.CallUse)]
    public async Task<ActionResult<CallStartedDto>> StartCall(
        [FromBody] StartCallRequest request, CancellationToken cancellationToken)
    {
        await EnsureModuleAsync(OrgModules.Calling, cancellationToken);
        return Ok(await _chat.StartCallAsync(request.CalleeEmployeeId, request.Kind, cancellationToken));
    }

    [HttpPost("calls/{id:guid}/end")]
    [Permission(Permissions.CallUse)]
    public async Task<IActionResult> EndCall(Guid id,
        [FromBody] EndCallRequest request, CancellationToken cancellationToken)
    {
        await EnsureModuleAsync(OrgModules.Calling, cancellationToken);
        await _chat.EndCallAsync(id, request.State, request.DurationSeconds, cancellationToken);
        return NoContent();
    }

    [HttpGet("calls/history")]
    [Permission(Permissions.CallUse)]
    public async Task<ActionResult<IReadOnlyList<CallHistoryItemDto>>> CallHistory(CancellationToken cancellationToken)
    {
        await EnsureModuleAsync(OrgModules.Calling, cancellationToken);
        return Ok(await _chat.GetCallHistoryAsync(cancellationToken));
    }
}
