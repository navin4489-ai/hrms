using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.Auditing;
using Wintech.Hrms.Api.Common.Files;
using Wintech.Hrms.Api.Common.Notifications;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Approvals;
using Wintech.Hrms.Api.Domain.Documents;

namespace Wintech.Hrms.Api.Modules.Documents;

/// <summary>
/// Bridges letter requests to the central approval engine (RequestType "Letter").
/// On approval the PDF is generated from verified employee data into the private document
/// store and linked via <see cref="LetterRequest.GeneratedDocumentId"/>; on cancellation the
/// request reverts to Cancelled. The employee is notified in both cases (FR-EMP-072/120).
/// </summary>
public sealed class LetterApprovalHandler : IApprovalHandler
{
    private const long MaxPdfBytes = 10 * 1024 * 1024;

    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly IClock _clock;
    private readonly IPrivateFileStore _files;
    private readonly INotificationPublisher _notifications;
    private readonly ILogger<LetterApprovalHandler> _logger;

    public LetterApprovalHandler(AppDbContext db, IAuditService audit, IClock clock,
        IPrivateFileStore files, INotificationPublisher notifications, ILogger<LetterApprovalHandler> logger)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
        _files = files;
        _notifications = notifications;
        _logger = logger;
    }

    public string RequestType => LetterService.LetterRequestType;

    public async Task OnCompletedAsync(ApprovalTask task, ApprovalActionType finalAction, string? comment,
        CancellationToken cancellationToken = default)
    {
        var letter = await _db.Set<LetterRequest>()
            .FirstOrDefaultAsync(r => r.Id == task.RequestId, cancellationToken);

        if (letter is null)
        {
            _logger.LogWarning("Letter request {RequestId} not found for approval task {TaskId}.", task.RequestId, task.Id);
            return;
        }

        if (letter.Status != LetterRequestStatus.Pending) return; // already processed (UAT-004: exactly once)

        if (finalAction == ApprovalActionType.Reject)
        {
            letter.Status = LetterRequestStatus.Rejected;
            letter.DecisionComment = comment;
            _audit.Add("LetterRequest.Reject", "LetterRequest", letter.Id.ToString(), "Rejected",
                new { letter.LetterType, Comment = comment });
            await _db.SaveChangesAsync(cancellationToken);
            await NotifyAsync(letter, "Your letter request was not approved",
                $"{LetterService.LetterTypeLabel(letter.LetterType)} request: {comment ?? "rejected by HR."}");
            return;
        }

        if (finalAction != ApprovalActionType.Approve)
        {
            // Returned or delegated: the request stays pending for the next workflow step.
            return;
        }

        letter.Status = LetterRequestStatus.Approved;
        letter.ApprovedAt = _clock.UtcNow;
        letter.DecisionComment = comment;

        await GenerateLetterPdfAsync(letter, cancellationToken);

        _audit.Add("LetterRequest.Approve", "LetterRequest", letter.Id.ToString(), "Success",
            new { letter.LetterType, DocumentId = letter.GeneratedDocumentId });
        await _db.SaveChangesAsync(cancellationToken);

        await NotifyAsync(letter, "Your letter is ready",
            $"{LetterService.LetterTypeLabel(letter.LetterType)} has been generated and is ready for download.");
    }

    public async Task OnCancelledAsync(ApprovalTask task, string? reason, CancellationToken cancellationToken = default)
    {
        var letter = await _db.Set<LetterRequest>()
            .FirstOrDefaultAsync(r => r.Id == task.RequestId, cancellationToken);

        if (letter is null || letter.Status != LetterRequestStatus.Pending) return;

        letter.Status = LetterRequestStatus.Cancelled;
        letter.DecisionComment = reason;

        _audit.Add("LetterRequest.Cancel", "LetterRequest", letter.Id.ToString(), "Cancelled",
            new { letter.LetterType, Reason = reason });
        await _db.SaveChangesAsync(cancellationToken);

        await NotifyAsync(letter, "Your letter request was cancelled",
            $"{LetterService.LetterTypeLabel(letter.LetterType)} request cancelled. {reason}".Trim());
    }

    /// <summary>Generates the letter PDF from verified data, stores it privately and links it to the request.</summary>
    private async Task GenerateLetterPdfAsync(LetterRequest letter, CancellationToken cancellationToken)
    {
        var employee = await _db.Employees.AsNoTracking()
            .Where(e => e.Id == letter.EmployeeId)
            .Select(e => new
            {
                e.EmployeeNumber,
                e.DisplayName,
                e.JoinDate,
                Designation = e.Designation != null ? e.Designation.Name : "",
                Department = e.Department != null ? e.Department.Name : "",
                Location = e.Location != null ? e.Location.Name : "",
                LegalEntity = e.Organization != null ? e.Organization.Name : ""
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"Employee {letter.EmployeeId} not found for letter {letter.Id}.");

        var now = _clock.UtcNow;
        var bytes = LetterPdf.Generate(new LetterPdfModel(
            employee.LegalEntity,
            employee.DisplayName,
            employee.EmployeeNumber,
            employee.Designation,
            employee.Department,
            employee.Location,
            employee.JoinDate,
            letter.LetterType,
            letter.Purpose,
            now));

        var fileName = $"{LetterService.LetterTypeLabel(letter.LetterType).Replace(' ', '-')}-{employee.EmployeeNumber}-{now:yyyyMMdd}.pdf";
        using var content = new MemoryStream(bytes);
        var stored = await _files.SaveAsync(content, fileName, MaxPdfBytes, [".pdf"], cancellationToken);

        var document = new EmployeeDocument
        {
            Id = Guid.NewGuid(),
            EmployeeId = letter.EmployeeId,
            Name = stored.OriginalFileName,
            Category = DocumentCategory.Employment,
            DocumentType = LetterService.LetterTypeLabel(letter.LetterType),
            IssuedOn = DateOnly.FromDateTime(now),
            Status = EmployeeDocumentStatus.Verified,
            VerificationNote = "System generated on approval.",
            VerifiedAt = now,
            SizeBytes = stored.SizeBytes,
            FileExtension = ".pdf",
            StorageKey = stored.StorageKey,
            ContentType = stored.ContentType,
            IsQuarantined = false,
            UploadedByUserId = null,
            UploadedAt = now
        };
        _db.Set<EmployeeDocument>().Add(document);

        letter.GeneratedDocumentId = document.Id;
        letter.GeneratedAt = now;
        letter.Status = LetterRequestStatus.Generated;
    }

    private async Task NotifyAsync(LetterRequest letter, string title, string body)
    {
        var userId = await _db.UserAccounts.AsNoTracking()
            .Where(u => u.EmployeeId == letter.EmployeeId)
            .Select(u => u.Id)
            .FirstOrDefaultAsync();

        await _notifications.PublishAsync(new NotificationMessage(
            userId == Guid.Empty ? null : userId,
            "Letter",
            title,
            body,
            DeepLink: "/letters",
            Category: "Documents",
            EmployeeId: letter.EmployeeId));
    }
}
