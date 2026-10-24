using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wintech.Hrms.Api.Common.ErrorHandling;
using Wintech.Hrms.Api.Domain.Documents;
using Wintech.Hrms.Api.Security;
using Wintech.Hrms.Api.Security.Auth;

namespace Wintech.Hrms.Api.Modules.Documents;

/// <summary>Employee documents (contract §11, FR-EMP-070, FR-ADM-080/084, BR-010).</summary>
[ApiController]
[Route("api/v1/documents")]
[Produces("application/json")]
public sealed class DocumentsController : ControllerBase
{
    private readonly IDocumentService _documents;

    public DocumentsController(IDocumentService documents) => _documents = documents;

    /// <summary>
    /// List documents — own by default; another employee's documents require document.manage
    /// and identity/medical additionally document.sensitive (FR-ADM-084).
    /// </summary>
    [HttpGet]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<EmployeeDocumentDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<EmployeeDocumentDto>>> List(
        [FromQuery] string? category,
        [FromQuery] string? status,
        [FromQuery] Guid? employeeId,
        CancellationToken cancellationToken)
        => Ok(await _documents.GetDocumentsAsync(category, status, employeeId, cancellationToken));

    /// <summary>
    /// Upload a document (multipart). Allow-list pdf/png/jpg/jpeg/docx/xlsx/txt, max size from
    /// configuration (default 10 MB — BR-010). New uploads are Pending verification.
    /// </summary>
    [HttpPost]
    [Authorize]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(EmployeeDocumentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<EmployeeDocumentDto>> Upload([FromForm] UploadDocumentRequest request, CancellationToken cancellationToken)
    {
        if (request.File is null || request.File.Length == 0)
            throw new RequestValidationException(errors: new()
            {
                ["file"] = ["A file is required."]
            });

        var document = await _documents.UploadAsync(
            request.File.OpenReadStream(), request.File.FileName, request.Category,
            request.DocumentType, request.IssuedOn, request.ExpiresOn, cancellationToken);

        return StatusCode(StatusCodes.Status201Created, document);
    }

    /// <summary>Download a document — streamed from private storage, never a public URL; access is audited.</summary>
    [HttpGet("{id:guid}/download")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var file = await _documents.DownloadAsync(id, cancellationToken);
        return File(file.Content, file.ContentType, file.FileName);
    }

    /// <summary>Verify or reject a document (FR-ADM-080; rejection requires a note — BR-007).</summary>
    [HttpPost("{id:guid}/decision")]
    [Authorize]
    [Permission(Permissions.DocumentManage)]
    [ProducesResponseType(typeof(EmployeeDocumentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<EmployeeDocumentDto>> Decision(Guid id, [FromBody] DocumentDecisionRequest request, CancellationToken cancellationToken)
        => Ok(await _documents.DecideAsync(id, request.Action, request.Note, cancellationToken));
}
