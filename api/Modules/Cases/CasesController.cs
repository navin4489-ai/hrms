using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wintech.Hrms.Api.Common.Paging;
using Wintech.Hrms.Api.Domain.Cases;
using Wintech.Hrms.Api.Security;
using Wintech.Hrms.Api.Security.Auth;

namespace Wintech.Hrms.Api.Modules.Cases;

/// <summary>HR helpdesk / employee CRM (contract §15 — FR-EMP-110..115, FR-ADM-120..125).</summary>
[ApiController]
[Route("api/v1/cases")]
[Authorize]
[Produces("application/json")]
public sealed class CasesController : ControllerBase
{
    private readonly ICaseService _cases;

    public CasesController(ICaseService cases) => _cases = cases;

    /// <summary>Service categories and subcategories (FR-EMP-111).</summary>
    [HttpGet("categories")]
    [ProducesResponseType(typeof(IReadOnlyList<CaseCategoryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CaseCategoryDto>>> GetCategories(CancellationToken cancellationToken)
        => Ok(await _cases.GetCategoriesAsync(cancellationToken));

    /// <summary>Paged case list; employees see their own cases, agents see their queues (FR-EMP-112).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<CaseListItem>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<CaseListItem>>> List(
        [FromQuery] PageRequest page,
        [FromQuery] CaseStatus? status,
        [FromQuery] string? category,
        [FromQuery] CasePriority? priority,
        [FromQuery] bool mine = false,
        [FromQuery] string? queue = null,
        CancellationToken cancellationToken = default)
        => Ok(await _cases.ListAsync(page, status, category, priority, mine, queue, cancellationToken));

    /// <summary>Raise a case (FR-EMP-110).</summary>
    [HttpPost]
    [ProducesResponseType(typeof(CaseCreatedResult), StatusCodes.Status201Created)]
    public async Task<ActionResult<CaseCreatedResult>> Create([FromBody] CreateCaseRequest request,
        CancellationToken cancellationToken)
    {
        var created = await _cases.CreateAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    /// <summary>
    /// Case detail with employee-visible messages and <c>canReply</c>. Confidential cases are 404
    /// outside the restricted queue (FR-EMP-114, UAT-009); internal notes are omitted for employees (FR-ADM-122).
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(CaseDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CaseDetailDto>> GetDetail(Guid id, CancellationToken cancellationToken)
    {
        var detail = await _cases.GetDetailAsync(id, cancellationToken);
        return Ok(detail);
    }

    /// <summary>Employee-visible reply (FR-EMP-113).</summary>
    [HttpPost("{id:guid}/messages")]
    [ProducesResponseType(typeof(CaseMessageDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<CaseMessageDto>> AddMessage(Guid id, [FromBody] AddCaseMessageRequest request,
        CancellationToken cancellationToken)
    {
        var message = await _cases.AddMessageAsync(id, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, message);
    }

    /// <summary>HR-internal note; never visible to employees (FR-ADM-122).</summary>
    [HttpPost("{id:guid}/notes")]
    [Permission(Permissions.CaseManage)]
    [ProducesResponseType(typeof(CaseMessageDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<CaseMessageDto>> AddNote(Guid id, [FromBody] AddCaseNoteRequest request,
        CancellationToken cancellationToken)
    {
        var message = await _cases.AddNoteAsync(id, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, message);
    }

    /// <summary>
    /// Resolve / Close (agent only) or Reopen (owner within the configured window) — FR-EMP-113.
    /// </summary>
    [HttpPost("{id:guid}/transition")]
    [ProducesResponseType(typeof(CaseDetailDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CaseDetailDto>> Transition(Guid id, [FromBody] CaseTransitionRequest request,
        CancellationToken cancellationToken)
        => Ok(await _cases.TransitionAsync(id, request, cancellationToken));

    /// <summary>Rate the resolution 1-5, once, after resolve (FR-EMP-113).</summary>
    [HttpPost("{id:guid}/rate")]
    [ProducesResponseType(typeof(CaseDetailDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CaseDetailDto>> Rate(Guid id, [FromBody] RateCaseRequest request,
        CancellationToken cancellationToken)
        => Ok(await _cases.RateAsync(id, request, cancellationToken));
}
