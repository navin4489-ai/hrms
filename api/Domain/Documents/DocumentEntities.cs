using Wintech.Hrms.Api.Domain.Employees;

namespace Wintech.Hrms.Api.Domain.Documents;

/// <summary>Document classification (contract §11). Identity and Medical are sensitive (FR-ADM-084).</summary>
public enum DocumentCategory
{
    Identity = 0,
    Education = 1,
    Employment = 2,
    Tax = 3,
    Medical = 4,
    Other = 5
}

/// <summary>
/// Stored verification state of an uploaded document. ExpiringSoon and Expired are derived from
/// <see cref="EmployeeDocument.ExpiresOn"/> at read time so ageing never requires a back-office job.
/// </summary>
public enum EmployeeDocumentStatus
{
    Pending = 0,
    Verified = 1,
    Rejected = 2,
    /// <summary>Derived: verified and expires within the expiry-warning window.</summary>
    ExpiringSoon = 3,
    /// <summary>Derived: verified and past its expiry date.</summary>
    Expired = 4
}

/// <summary>Letter request lifecycle; the PDF is generated when the approval workflow completes (FR-EMP-072).</summary>
public enum LetterRequestStatus
{
    Pending = 0,
    Approved = 1,
    Generated = 2,
    Rejected = 3,
    Cancelled = 4
}

/// <summary>Employee-uploaded document metadata; bytes live in private storage (never a public URL).</summary>
public class EmployeeDocument
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    /// <summary>Original file name as uploaded by the employee.</summary>
    public string Name { get; set; } = string.Empty;
    public DocumentCategory Category { get; set; }
    /// <summary>Concrete type inside the category, e.g. "Aadhaar Card", "Passport".</summary>
    public string DocumentType { get; set; } = string.Empty;
    public DateOnly? IssuedOn { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    /// <summary>Stored state (Pending/Verified/Rejected); ExpiringSoon/Expired are derived at read time.</summary>
    public EmployeeDocumentStatus Status { get; set; } = EmployeeDocumentStatus.Pending;
    public string? VerificationNote { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public Guid? VerifiedByUserId { get; set; }
    public long SizeBytes { get; set; }
    public string FileExtension { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    /// <summary>BR-010: quarantined until security checks complete (cleared automatically when configured).</summary>
    public bool IsQuarantined { get; set; }
    public Guid? UploadedByUserId { get; set; }
    public DateTime UploadedAt { get; set; }
}

/// <summary>Employee request for an HR letter, routed through approvals (FR-EMP-071/072).</summary>
public class LetterRequest
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    /// <summary>EmploymentCertificate | SalaryCertificate | ExperienceLetter | AddressProof.</summary>
    public string LetterType { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public LetterRequestStatus Status { get; set; } = LetterRequestStatus.Pending;
    public Guid? ApprovalTaskId { get; set; }
    /// <summary>EmployeeDocument (category Employment) holding the generated PDF.</summary>
    public Guid? GeneratedDocumentId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? GeneratedAt { get; set; }
    public string? DecisionComment { get; set; }
}

/// <summary>Published company policy / handbook version (FR-ADM-081/083).</summary>
public class CompanyPolicy
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    /// <summary>Free-form classification, e.g. HR, Security, Conduct, Workplace.</summary>
    public string Category { get; set; } = string.Empty;
    public string? Summary { get; set; }
    /// <summary>Short policy text returned by GET /policies/{id}.</summary>
    public string? Body { get; set; }
    public string Version { get; set; } = "1.0";
    public bool RequiresAcknowledgement { get; set; }
    public DateTime PublishedAt { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Optional full-text document in private storage.</summary>
    public Guid? DocumentId { get; set; }
}

/// <summary>Versioned policy acknowledgement per employee (FR-EMP-074, FR-ADM-083).</summary>
public class PolicyAcknowledgement
{
    public Guid Id { get; set; }
    public Guid PolicyId { get; set; }
    public CompanyPolicy? Policy { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid UserId { get; set; }
    /// <summary>The policy version that was acknowledged; acknowledgements are never mutated.</summary>
    public string PolicyVersion { get; set; } = string.Empty;
    public DateTime AcknowledgedAt { get; set; }
    public string? Comment { get; set; }
}
