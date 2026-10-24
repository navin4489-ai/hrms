using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wintech.Hrms.Api.Common.Paging;

namespace Wintech.Hrms.Api.Modules.Notifications;

/// <summary>Notification inbox (contract §16 — FR-EMP-120/122).</summary>
[ApiController]
[Route("api/v1/notifications")]
[Authorize]
[Produces("application/json")]
public sealed class NotificationsController : ControllerBase
{
    private readonly INotificationService _notifications;

    public NotificationsController(INotificationService notifications) => _notifications = notifications;

    /// <summary>The caller's notification inbox, newest first.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<NotificationDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<NotificationDto>>> List([FromQuery] bool unreadOnly = false,
        [FromQuery] PageRequest? page = null, CancellationToken cancellationToken = default)
        => Ok(await _notifications.ListAsync(unreadOnly, page ?? PageRequest.Of(), cancellationToken));

    /// <summary>Unread badge count shown on the home screen.</summary>
    [HttpGet("unread-count")]
    [ProducesResponseType(typeof(UnreadCountResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<UnreadCountResult>> UnreadCount(CancellationToken cancellationToken)
        => Ok(await _notifications.UnreadCountAsync(cancellationToken));

    /// <summary>Mark one notification as read.</summary>
    [HttpPost("{id:guid}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken)
    {
        await _notifications.MarkReadAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Mark every unread notification as read.</summary>
    [HttpPost("read-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
    {
        await _notifications.MarkAllReadAsync(cancellationToken);
        return NoContent();
    }
}
