using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Wintech.Hrms.Api.Modules.Documents;

/// <summary>Letter requests (contract §11, FR-EMP-071/072) — mine only; approved letters become PDFs.</summary>
[ApiController]
[Route("api/v1/letters/requests")]
[Produces("application/json")]
public sealed class LettersController : ControllerBase
{
    private readonly ILetterService _letters;

    public LettersController(ILetterService letters) => _letters = letters;

    /// <summary>List the caller's letter requests with their approval task and generated document.</summary>
    [HttpGet]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<LetterRequestDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<LetterRequestDto>>> List(CancellationToken cancellationToken)
        => Ok(await _letters.GetMineAsync(cancellationToken));

    /// <summary>
    /// Request a letter. Starts the "Letter" approval workflow and returns 201 with the
    /// approvalTaskId; the PDF is generated once the workflow completes (FR-EMP-071/072).
    /// </summary>
    [HttpPost]
    [Authorize]
    [ProducesResponseType(typeof(CreateLetterResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CreateLetterResponse>> Create([FromBody] CreateLetterRequest request, CancellationToken cancellationToken)
    {
        var response = await _letters.CreateAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    /// <summary>Download the generated letter PDF (409 until the request is approved and generated).</summary>
    [HttpGet("{id:guid}/download")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var file = await _letters.DownloadAsync(id, cancellationToken);
        return File(file.Content, file.ContentType, file.FileName);
    }
}
