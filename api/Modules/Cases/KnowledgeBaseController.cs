using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Wintech.Hrms.Api.Modules.Cases;

/// <summary>Searchable knowledge base / FAQs (contract §15 — FR-EMP-115, FR-ADM-125).</summary>
[ApiController]
[Route("api/v1/knowledge-base")]
[Authorize]
[Produces("application/json")]
public sealed class KnowledgeBaseController : ControllerBase
{
    private readonly ICaseService _cases;

    public KnowledgeBaseController(ICaseService cases) => _cases = cases;

    /// <summary>Search articles by text and/or category.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<KnowledgeArticleListItem>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<KnowledgeArticleListItem>>> Search([FromQuery] string? q,
        [FromQuery] string? category, CancellationToken cancellationToken)
        => Ok(await _cases.SearchKnowledgeBaseAsync(q, category, cancellationToken));

    /// <summary>Article detail with full body and version.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(KnowledgeArticleDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<KnowledgeArticleDetail>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await _cases.GetKnowledgeArticleAsync(id, cancellationToken));
}
