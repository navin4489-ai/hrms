using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wintech.Hrms.Api.Common.Paging;
using Wintech.Hrms.Api.Domain.Announcements;
using Wintech.Hrms.Api.Security;
using Wintech.Hrms.Api.Security.Auth;

namespace Wintech.Hrms.Api.Modules.Announcements;

/// <summary>Announcements &amp; notifications (contract §16 — FR-EMP-120..124, FR-ADM-130..133).</summary>
[ApiController]
[Route("api/v1/announcements")]
[Authorize]
[Produces("application/json")]
public sealed class AnnouncementsController : ControllerBase
{
    private readonly IAnnouncementService _announcements;

    public AnnouncementsController(IAnnouncementService announcements) => _announcements = announcements;

    /// <summary>Announcements targeted at the caller, pinned first (FR-EMP-123).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<AnnouncementDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AnnouncementDto>>> List(
        [FromQuery] AnnouncementCategory? category, [FromQuery] PageRequest page, CancellationToken cancellationToken)
        => Ok(await _announcements.ListAsync(category, page, cancellationToken));

    /// <summary>Announcement detail — 404 when targeted at another population.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AnnouncementDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AnnouncementDto>> GetDetail(Guid id, CancellationToken cancellationToken)
        => Ok(await _announcements.GetDetailAsync(id, cancellationToken));

    /// <summary>Acknowledge a mandatory announcement (FR-ADM-132).</summary>
    [HttpPost("{id:guid}/acknowledge")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Acknowledge(Guid id, CancellationToken cancellationToken)
    {
        await _announcements.AcknowledgeAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Create an announcement draft (FR-ADM-130).</summary>
    [HttpPost]
    [Permission(Permissions.AnnouncementCreate)]
    [ProducesResponseType(typeof(AnnouncementDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<AnnouncementDto>> Create([FromBody] CreateAnnouncementRequest request,
        CancellationToken cancellationToken)
    {
        var created = await _announcements.CreateAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    /// <summary>Publish a draft announcement; fans notifications out to the audience (FR-ADM-130/133).</summary>
    [HttpPost("{id:guid}/publish")]
    [Permission(Permissions.AnnouncementCreate)]
    [ProducesResponseType(typeof(AnnouncementDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AnnouncementDto>> Publish(Guid id, CancellationToken cancellationToken)
        => Ok(await _announcements.PublishAsync(id, cancellationToken));
}
