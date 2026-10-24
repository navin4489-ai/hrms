using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wintech.Hrms.Api.Common.Auditing;
using Wintech.Hrms.Api.Common.Configuration;
using Wintech.Hrms.Api.Common.CurrentUser;
using Wintech.Hrms.Api.Common.ErrorHandling;
using Wintech.Hrms.Api.Common.Files;
using Wintech.Hrms.Api.Common.Registration;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Documents;
using Wintech.Hrms.Api.Security;

namespace Wintech.Hrms.Api.Modules.Documents;

/// <summary>
/// Employee document management (contract §11, FR-EMP-070, FR-ADM-080/084, BR-010).
/// Files live in private storage; downloads are streamed and audited — never public URLs.
/// </summary>
public interface IDocumentService : IAppService
{
    Task<IReadOnlyList<EmployeeDocumentDto>> GetDocumentsAsync(string? category, string? status, Guid? employeeId, CancellationToken cancellationToken = default);
    Task<EmployeeDocumentDto> UploadAsync(Stream content, string fileName, DocumentCategory category, string documentType, DateOnly? issuedOn, DateOnly? expiresOn, CancellationToken cancellationToken = default);
    Task<FileDownload> DownloadAsync(Guid id, CancellationToken cancellationToken = default);
    Task<EmployeeDocumentDto> DecideAsync(Guid id, DocumentDecisionAction action, string? note, CancellationToken cancellationToken = default);
}

public sealed class DocumentService : IDocumentService
{
    /// <summary>Verified documents expiring within this window surface as ExpiringSoon (FR-EMP-073).</summary>
    private const int ExpiryWarningDays = 30;

    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;
    private readonly IClock _clock;
    private readonly IPrivateFileStore _files;
    private readonly SecurityOptions _security;

    public DocumentService(AppDbContext db, ICurrentUser currentUser, IAuditService audit,
        IClock clock, IPrivateFileStore files, IOptions<SecurityOptions> security)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
        _files = files;
        _security = security.Value;
    }

    public async Task<IReadOnlyList<EmployeeDocumentDto>> GetDocumentsAsync(
        string? category, string? status, Guid? employeeId, CancellationToken cancellationToken = default)
    {
        _currentUser.EnsureActive();
        var own = RequireEmployeeId();
        var target = employeeId ?? own;

        // Another employee's documents require document.manage (no existence leak otherwise -> 404).
        var viewingOther = target != own;
        if (viewingOther && !_currentUser.HasPermission(Permissions.DocumentManage))
            throw new NotFoundException("The requested document was not found.");

        var canManage = _currentUser.HasPermission(Permissions.DocumentManage);
        var canSensitive = _currentUser.HasPermission(Permissions.DocumentSensitive);

        DocumentCategory? categoryFilter = null;
        if (!string.IsNullOrWhiteSpace(category))
        {
            if (!Enum.TryParse<DocumentCategory>(category, ignoreCase: true, out var parsed))
                throw new RequestValidationException(errors: new()
                {
                    ["category"] = [$"Allowed values: {string.Join(", ", Enum.GetNames<DocumentCategory>())}."]
                });
            categoryFilter = parsed;
        }

        EmployeeDocumentStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<EmployeeDocumentStatus>(status, ignoreCase: true, out var parsed))
                throw new RequestValidationException(errors: new()
                {
                    ["status"] = [$"Allowed values: {string.Join(", ", Enum.GetNames<EmployeeDocumentStatus>())}."]
                });
            statusFilter = parsed;
        }

        var today = _clock.Today;
        var soonBy = today.AddDays(ExpiryWarningDays);

        var query = _db.Set<EmployeeDocument>().AsNoTracking().Where(d => d.EmployeeId == target);
        if (categoryFilter is { } categoryValue) query = query.Where(d => d.Category == categoryValue);

        if (statusFilter is { } wanted)
        {
            query = wanted switch
            {
                EmployeeDocumentStatus.Pending => query.Where(d => d.Status == EmployeeDocumentStatus.Pending),
                EmployeeDocumentStatus.Rejected => query.Where(d => d.Status == EmployeeDocumentStatus.Rejected),
                EmployeeDocumentStatus.Verified => query.Where(d => d.Status == EmployeeDocumentStatus.Verified &&
                                                                  (d.ExpiresOn == null || d.ExpiresOn > soonBy)),
                EmployeeDocumentStatus.ExpiringSoon => query.Where(d => d.Status == EmployeeDocumentStatus.Verified &&
                                                                       d.ExpiresOn != null && d.ExpiresOn >= today && d.ExpiresOn <= soonBy),
                EmployeeDocumentStatus.Expired => query.Where(d => d.Status == EmployeeDocumentStatus.Verified &&
                                                                  d.ExpiresOn != null && d.ExpiresOn < today),
                _ => query
            };
        }

        var rows = await query
            .OrderByDescending(d => d.UploadedAt)
            .ToListAsync(cancellationToken);

        // FR-ADM-084: identity/medical documents of another employee additionally need document.sensitive.
        return rows
            .Where(d => !viewingOther || canSensitive || !IsSensitive(d.Category))
            .Select(d => Map(d, own, canManage, today))
            .ToList();
    }

    public async Task<EmployeeDocumentDto> UploadAsync(Stream content, string fileName,
        DocumentCategory category, string documentType, DateOnly? issuedOn, DateOnly? expiresOn,
        CancellationToken cancellationToken = default)
    {
        _currentUser.EnsureActive();
        var employeeId = RequireEmployeeId();

        if (!Enum.IsDefined(category))
            throw new RequestValidationException(errors: new()
            {
                ["category"] = [$"Allowed values: {string.Join(", ", Enum.GetNames<DocumentCategory>())}."]
            });

        if (issuedOn is not null && expiresOn is not null && expiresOn < issuedOn)
            throw new RequestValidationException(errors: new()
            {
                ["expiresOn"] = ["Expiry date must be on or after the issue date."]
            });

        // BR-010: allow-list + max size come from configuration (default 10 MB).
        var stored = await _files.SaveAsync(content, fileName, _security.MaxUploadBytes,
            _security.AllowedFileExtensions, cancellationToken);

        var document = new EmployeeDocument
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            Name = stored.OriginalFileName,
            Category = category,
            DocumentType = documentType.Trim(),
            IssuedOn = issuedOn,
            ExpiresOn = expiresOn,
            Status = EmployeeDocumentStatus.Pending,
            SizeBytes = stored.SizeBytes,
            FileExtension = Path.GetExtension(stored.OriginalFileName).ToLowerInvariant(),
            StorageKey = stored.StorageKey,
            ContentType = stored.ContentType,
            // Quarantined until cleared; AutoClearUploads (default true) clears on upload (BR-010).
            IsQuarantined = !_security.AutoClearUploads,
            UploadedByUserId = _currentUser.UserId,
            UploadedAt = _clock.UtcNow
        };

        _db.Set<EmployeeDocument>().Add(document);
        _audit.Add("Document.Upload", "EmployeeDocument", document.Id.ToString(),
            details: new { document.Name, Category = category.ToString(), document.DocumentType, document.SizeBytes });
        await _db.SaveChangesAsync(cancellationToken);

        return Map(document, employeeId, canManage: true, _clock.Today);
    }

    public async Task<FileDownload> DownloadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _currentUser.EnsureActive();
        var document = await LoadForAccessAsync(id, cancellationToken);

        if (document.IsQuarantined)
            throw new ConflictException("The document is quarantined pending security checks and cannot be downloaded yet.");

        var stream = await _files.OpenReadAsync(document.StorageKey, cancellationToken);

        // FR-EMP-062 / FR-ADM-152: every document download is an audited, authenticated read.
        await _audit.RecordAsync("Document.Download", "EmployeeDocument", document.Id.ToString(),
            details: new
            {
                document.Name,
                Category = document.Category.ToString(),
                document.DocumentType,
                Scope = document.EmployeeId == _currentUser.EmployeeId ? "Own" : "Privileged"
            }, cancellationToken: cancellationToken);

        return new FileDownload(stream.Content, stream.ContentType, document.Name);
    }

    public async Task<EmployeeDocumentDto> DecideAsync(Guid id, DocumentDecisionAction action, string? note, CancellationToken cancellationToken = default)
    {
        var document = await _db.Set<EmployeeDocument>()
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken)
            ?? throw new NotFoundException("The requested document was not found.");

        if (action == DocumentDecisionAction.Reject && string.IsNullOrWhiteSpace(note))
            throw new BusinessRuleException("A note is required when rejecting a document.",
                new() { ["note"] = ["Provide the reason for rejection (BR-007)."] });

        var now = _clock.UtcNow;
        document.Status = action == DocumentDecisionAction.Verify
            ? EmployeeDocumentStatus.Verified
            : EmployeeDocumentStatus.Rejected;
        document.VerificationNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        document.VerifiedAt = now;
        document.VerifiedByUserId = _currentUser.UserId;

        _audit.Add("Document.Decision", "EmployeeDocument", document.Id.ToString(),
            details: new { Action = action.ToString(), Note = document.VerificationNote, document.Name });
        await _db.SaveChangesAsync(cancellationToken);

        return Map(document, _currentUser.EmployeeId ?? document.EmployeeId,
            _currentUser.HasPermission(Permissions.DocumentManage), _clock.Today);
    }

    /// <summary>Identity and medical documents are sensitive (FR-ADM-084, BR-009).</summary>
    private static bool IsSensitive(DocumentCategory category) =>
        category is DocumentCategory.Identity or DocumentCategory.Medical;

    private Guid RequireEmployeeId() =>
        _currentUser.EmployeeId
        ?? throw new ForbiddenException("No employee record is linked to your account.");

    /// <summary>
    /// Loads a document enforcing record scope: the owner always reaches their own documents;
    /// another employee's documents need document.manage, identity/medical additionally
    /// document.sensitive (FR-ADM-084). Out-of-scope records are 404 (no existence leak).
    /// </summary>
    private async Task<EmployeeDocument> LoadForAccessAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await _db.Set<EmployeeDocument>().AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken)
            ?? throw new NotFoundException("The requested document was not found.");

        var own = document.EmployeeId == _currentUser.EmployeeId;
        if (own) return document;

        if (!_currentUser.HasPermission(Permissions.DocumentManage))
            throw new NotFoundException("The requested document was not found.");

        if (IsSensitive(document.Category) && !_currentUser.HasPermission(Permissions.DocumentSensitive))
            throw new NotFoundException("The requested document was not found.");

        return document;
    }

    private static EmployeeDocumentDto Map(EmployeeDocument document, Guid viewerEmployeeId, bool canManage, DateOnly today)
    {
        var own = document.EmployeeId == viewerEmployeeId;
        var status = EffectiveStatus(document, today);
        var canDownload = !document.IsQuarantined && (own || canManage);
        return new EmployeeDocumentDto(
            document.Id,
            document.Name,
            document.Category,
            document.DocumentType,
            document.IssuedOn,
            document.ExpiresOn,
            status,
            document.VerificationNote,
            document.SizeBytes,
            document.FileExtension,
            canDownload,
            document.UploadedAt);
    }

    /// <summary>Verified documents age into ExpiringSoon/Expired from their expiry date (FR-EMP-073).</summary>
    internal static EmployeeDocumentStatus EffectiveStatus(EmployeeDocument document, DateOnly today)
    {
        if (document.Status != EmployeeDocumentStatus.Verified) return document.Status;
        if (document.ExpiresOn is not { } expires) return EmployeeDocumentStatus.Verified;
        if (expires < today) return EmployeeDocumentStatus.Expired;
        return expires <= today.AddDays(ExpiryWarningDays)
            ? EmployeeDocumentStatus.ExpiringSoon
            : EmployeeDocumentStatus.Verified;
    }
}
