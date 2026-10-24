using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Wintech.Hrms.Api.Domain.Documents;

namespace Wintech.Hrms.Api.Modules.Documents;

// ---------------------------------------------------------------- employee documents

/// <summary>ListItem for GET /documents and 201 body of POST /documents (contract §11).</summary>
public sealed record EmployeeDocumentDto(
    Guid Id,
    string Name,
    DocumentCategory Category,
    string DocumentType,
    DateOnly? IssuedOn,
    DateOnly? ExpiresOn,
    EmployeeDocumentStatus Status,
    string? VerificationNote,
    long SizeBytes,
    string FileExtension,
    bool CanDownload,
    DateTime UploadedAt);

/// <summary>Multipart form for POST /documents: file + category + documentType + issuedOn? + expiresOn? (BR-010).</summary>
public sealed class UploadDocumentRequest
{
    [Required]
    public IFormFile? File { get; set; }

    [Required]
    public DocumentCategory Category { get; set; }

    [Required(AllowEmptyStrings = false)]
    [StringLength(100)]
    public string DocumentType { get; set; } = string.Empty;

    public DateOnly? IssuedOn { get; set; }
    public DateOnly? ExpiresOn { get; set; }
}

/// <summary>Body for POST /documents/{id}/decision: Verify | Reject + note (FR-ADM-080, BR-007).</summary>
public sealed class DocumentDecisionRequest
{
    [Required]
    public DocumentDecisionAction Action { get; set; }

    /// <summary>Required when rejecting (BR-007).</summary>
    public string? Note { get; set; }
}

public enum DocumentDecisionAction
{
    Verify = 0,
    Reject = 1
}

/// <summary>Streamed file payload for private downloads (never a public URL).</summary>
public sealed record FileDownload(Stream Content, string ContentType, string FileName);

// ---------------------------------------------------------------- letters

/// <summary>ListItem for GET /letters/requests (contract §11, mine only).</summary>
public sealed record LetterRequestDto(
    Guid Id,
    string LetterType,
    string Purpose,
    LetterRequestStatus Status,
    DateTime CreatedAt,
    Guid? ApprovalTaskId,
    Guid? GeneratedDocumentId);

/// <summary>Body for POST /letters/requests (FR-EMP-071/072).</summary>
public sealed class CreateLetterRequest
{
    /// <summary>EmploymentCertificate | SalaryCertificate | ExperienceLetter | AddressProof.</summary>
    [Required(AllowEmptyStrings = false)]
    [StringLength(50)]
    public string LetterType { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    [StringLength(500)]
    public string Purpose { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Notes { get; set; }
}

/// <summary>201 body of POST /letters/requests (contract §11).</summary>
public sealed record CreateLetterResponse(Guid Id, Guid? ApprovalTaskId, string Status);

// ---------------------------------------------------------------- policies

/// <summary>ListItem for GET /policies (contract §11).</summary>
public sealed record CompanyPolicyDto(
    Guid Id,
    string Title,
    string Category,
    string Version,
    DateTime PublishedAt,
    DateOnly EffectiveFrom,
    bool RequiresAcknowledgement,
    DateTime? AcknowledgedAt,
    Guid? DocumentId);

/// <summary>Detail for GET /policies/{id} — includes the short policy body (contract §11).</summary>
public sealed record PolicyDetailDto(
    Guid Id,
    string Title,
    string Category,
    string? Summary,
    string? Body,
    string Version,
    DateTime PublishedAt,
    DateOnly EffectiveFrom,
    bool RequiresAcknowledgement,
    DateTime? AcknowledgedAt,
    Guid? DocumentId);

/// <summary>Body for POST /policies/{id}/acknowledge (FR-EMP-074).</summary>
public sealed class AcknowledgePolicyRequest
{
    [StringLength(1000)]
    public string? Comment { get; set; }
}

/// <summary>200 body of POST /policies/{id}/acknowledge — acknowledgement is versioned.</summary>
public sealed record AcknowledgePolicyResponse(DateTime AcknowledgedAt, string Version);
