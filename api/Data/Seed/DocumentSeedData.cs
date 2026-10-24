using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using Wintech.Hrms.Api.Common.Files;
using Wintech.Hrms.Api.Common.Registration;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Approvals;
using Wintech.Hrms.Api.Domain.Documents;

namespace Wintech.Hrms.Api.Data.Seed;

/// <summary>
/// Idempotent documents/letters/policies seed (Order 121): a handful of employee documents
/// across every status (Verified / Pending / ExpiringSoon / Expired / Rejected), company
/// policies with versioned acknowledgements for a couple of demo users, and one example
/// letter request. All files are stored through <see cref="IPrivateFileStore"/>.
/// </summary>
public sealed class DocumentSeedData : IDataSeeder
{
    private const long MaxSeedFileBytes = 10 * 1024 * 1024;

    private readonly AppDbContext _db;
    private readonly IClock _clock;
    private readonly IPrivateFileStore _files;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DocumentSeedData> _logger;

    public DocumentSeedData(AppDbContext db, IClock clock, IPrivateFileStore files,
        IServiceProvider serviceProvider, ILogger<DocumentSeedData> logger)
    {
        _db = db;
        _clock = clock;
        _files = files;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public int Order => 121;

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await SeedDocumentsAsync(cancellationToken);
        await SeedPoliciesAsync(cancellationToken);
        await SeedLetterRequestAsync(cancellationToken);
    }

    private async Task SeedDocumentsAsync(CancellationToken cancellationToken)
    {
        if (await _db.Set<EmployeeDocument>().AsNoTracking().AnyAsync(cancellationToken)) return;

        var today = _clock.Today;
        var employees = await _db.Employees.AsNoTracking()
            .OrderBy(e => e.EmployeeNumber)
            .Select(e => new { e.Id, e.EmployeeNumber, JoinDate = e.JoinDate })
            .ToListAsync(cancellationToken);

        var documents = new List<(Guid EmployeeId, string Number, string Name, DocumentCategory Category,
            string DocumentType, DateOnly? IssuedOn, DateOnly? ExpiresOn, EmployeeDocumentStatus Status, string? Note)>();

        foreach (var employee in employees)
        {
            // Standard set for every employee.
            documents.Add((employee.Id, employee.EmployeeNumber, $"Aadhaar-{employee.EmployeeNumber}.pdf",
                DocumentCategory.Identity, "Aadhaar Card", employee.JoinDate, null,
                EmployeeDocumentStatus.Verified, null));
            documents.Add((employee.Id, employee.EmployeeNumber, $"PAN-{employee.EmployeeNumber}.pdf",
                DocumentCategory.Tax, "PAN Card", employee.JoinDate, null,
                EmployeeDocumentStatus.Verified, null));
            documents.Add((employee.Id, employee.EmployeeNumber, $"Employment-Agreement-{employee.EmployeeNumber}.pdf",
                DocumentCategory.Employment, "Employment Agreement", employee.JoinDate, null,
                EmployeeDocumentStatus.Verified, "Onboarding copy, verified by HR."));
        }

        void Extra(string number, string name, DocumentCategory category, string documentType,
            DateOnly? issuedOn, DateOnly? expiresOn, EmployeeDocumentStatus status, string? note)
        {
            var employee = employees.First(e => e.EmployeeNumber == number);
            documents.Add((employee.Id, number, name, category, documentType, issuedOn, expiresOn, status, note));
        }

        // Priya Nair (demo "employee"): full document lifecycle samples.
        Extra("EMP006", "Passport-EMP006.pdf", DocumentCategory.Identity, "Passport",
            new DateOnly(2020, 4, 18), new DateOnly(2030, 4, 18), EmployeeDocumentStatus.Verified, null);
        Extra("EMP006", "Offer-Letter-EMP006.pdf", DocumentCategory.Employment, "Offer Letter",
            new DateOnly(2022, 6, 1), null, EmployeeDocumentStatus.Verified, null);
        Extra("EMP006", "Address-Proof-EMP006.pdf", DocumentCategory.Other, "Address Proof",
            new DateOnly(2026, 9, 1), null, EmployeeDocumentStatus.Rejected,
            "The uploaded copy is illegible. Please re-upload a clear photograph.");
        Extra("EMP006", "Vaccination-Certificate-EMP006.pdf", DocumentCategory.Medical, "Vaccination Certificate",
            new DateOnly(2025, 12, 1), new DateOnly(2027, 1, 31), EmployeeDocumentStatus.Pending, null);

        // Rohit Verma (demo "employee2"): passport expiring soon (FR-EMP-073), degree verified.
        Extra("EMP007", "Passport-EMP007.pdf", DocumentCategory.Identity, "Passport",
            new DateOnly(2016, 11, 10), today.AddDays(18), EmployeeDocumentStatus.Verified, null);
        Extra("EMP007", "Degree-Certificate-EMP007.pdf", DocumentCategory.Education, "Degree Certificate",
            new DateOnly(2021, 5, 20), null, EmployeeDocumentStatus.Verified, null);

        // Expired and pending samples on other employees.
        Extra("EMP009", "Work-Visa-EMP009.pdf", DocumentCategory.Other, "Work Visa",
            new DateOnly(2024, 10, 1), today.AddDays(-24), EmployeeDocumentStatus.Verified,
            "Renewal reminder sent to the employee.");
        Extra("EMP010", "Medical-Fitness-EMP010.pdf", DocumentCategory.Medical, "Medical Fitness Certificate",
            new DateOnly(2026, 8, 1), today.AddDays(200), EmployeeDocumentStatus.Pending, null);

        foreach (var doc in documents)
        {
            var content = new MemoryStream(BuildPlaceholderPdf(doc.DocumentType, doc.Number));
            var stored = await _files.SaveAsync(content, doc.Name, MaxSeedFileBytes, [".pdf"], cancellationToken);

            _db.Set<EmployeeDocument>().Add(new EmployeeDocument
            {
                Id = Guid.NewGuid(),
                EmployeeId = doc.EmployeeId,
                Name = stored.OriginalFileName,
                Category = doc.Category,
                DocumentType = doc.DocumentType,
                IssuedOn = doc.IssuedOn,
                ExpiresOn = doc.ExpiresOn,
                Status = doc.Status,
                VerificationNote = doc.Note,
                VerifiedAt = doc.Status is EmployeeDocumentStatus.Verified or EmployeeDocumentStatus.Rejected
                    ? DateTime.UtcNow
                    : null,
                SizeBytes = stored.SizeBytes,
                FileExtension = ".pdf",
                StorageKey = stored.StorageKey,
                ContentType = stored.ContentType,
                IsQuarantined = false,
                UploadedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Seeded {Count} employee documents across all statuses.", documents.Count);
    }

    private async Task SeedPoliciesAsync(CancellationToken cancellationToken)
    {
        if (!await _db.Set<CompanyPolicy>().AsNoTracking().AnyAsync(cancellationToken))
        {
            _db.Set<CompanyPolicy>().AddRange(
                NewPolicy("Employee Handbook 2026", "HR", "2.1",
                    "Everything you need to know about working at Wintech Solutions — organisation, benefits, working norms, and how HR processes work.",
                    """
                    The Employee Handbook describes the company's working norms, working hours, leave entitlements, benefits and the day-to-day
                    HR processes that apply to every employee. It supersedes all previous handbook versions. Employees are expected to read the
                    handbook in full and acknowledge the current version; the acknowledged version is recorded against your profile.
                    """,
                    requiresAcknowledgement: true, new DateOnly(2026, 1, 1), new DateTime(2025, 12, 15, 9, 0, 0, DateTimeKind.Utc)),
                NewPolicy("Information Security Policy", "Security", "3.0",
                    "Mandatory controls for protecting company and customer data — acceptable use, access control, incident reporting.",
                    """
                    All employees must protect company and customer information using strong unique passwords, multi-factor authentication where
                    enabled, and encrypted devices. Sensitive files must only be stored in approved private storage. Suspected security incidents
                    must be reported to IT within 24 hours. This policy applies to permanent employees, contractors and interns.
                    """,
                    requiresAcknowledgement: true, new DateOnly(2026, 3, 1), new DateTime(2026, 2, 10, 9, 0, 0, DateTimeKind.Utc)),
                NewPolicy("Leave & Attendance Policy", "HR", "1.4",
                    "Leave types, entitlements, accrual, attendance correction windows and approval flows.",
                    """
                    Full-time employees are entitled to casual, sick and earned leave as per the schedule in the HR portal. Attendance
                    corrections must be raised within 30 days of the date in question. Leave during notice period requires manager and HR
                    approval.
                    """,
                    requiresAcknowledgement: false, new DateOnly(2025, 7, 1), new DateTime(2025, 6, 20, 9, 0, 0, DateTimeKind.Utc)),
                NewPolicy("Code of Conduct", "Conduct", "2.0",
                    "Professional behaviour, anti-harassment, conflicts of interest and whistleblower protection.",
                    """
                    Wintech Solutions is committed to a respectful, harassment-free workplace. This code sets out expected behaviour, how to
                    raise concerns, and the discipline process for violations. Retaliation against good-faith reporters is prohibited.
                    """,
                    requiresAcknowledgement: true, new DateOnly(2025, 1, 1), new DateTime(2024, 12, 10, 9, 0, 0, DateTimeKind.Utc)),
                NewPolicy("Remote Work Policy", "Workplace", "1.1",
                    "Hybrid working expectations, equipment, home-office allowance and data-security duties while remote.",
                    """
                    Employees approved for hybrid work must be reachable during core hours, attend designated in-office days, and follow the
                    information-security policy from their home setup. A one-time home-office allowance is available per policy schedule.
                    """,
                    requiresAcknowledgement: false, new DateOnly(2026, 6, 1), new DateTime(2026, 5, 12, 9, 0, 0, DateTimeKind.Utc)));
            await _db.SaveChangesAsync(cancellationToken);
        }

        if (await _db.Set<PolicyAcknowledgement>().AsNoTracking().AnyAsync(cancellationToken)) return;

        async Task<(Guid EmployeeId, Guid UserId)> UserAsync(string userName)
        {
            var row = await _db.UserAccounts.AsNoTracking()
                .Where(u => u.UserName == userName && u.EmployeeId != null)
                .Select(u => new { EmployeeId = u.EmployeeId!.Value, u.Id })
                .FirstOrDefaultAsync(cancellationToken);
            return row is null ? (Guid.Empty, Guid.Empty) : (row.EmployeeId, row.Id);
        }

        async Task AcknowledgeAsync(string userName, string policyTitle, string version, DateTime at)
        {
            var (employeeId, userId) = await UserAsync(userName);
            if (employeeId == Guid.Empty) return;
            var policy = await _db.Set<CompanyPolicy>().AsNoTracking()
                .FirstOrDefaultAsync(p => p.Title == policyTitle, cancellationToken);
            if (policy is null) return;

            _db.Set<PolicyAcknowledgement>().Add(new PolicyAcknowledgement
            {
                Id = Guid.NewGuid(),
                PolicyId = policy.Id,
                EmployeeId = employeeId,
                UserId = userId,
                PolicyVersion = version,
                AcknowledgedAt = at
            });
        }

        // A couple of demo users already acknowledged some policies; the rest still require
        // acknowledgement so POST /policies/{id}/acknowledge and its 409 on repeat are demoable.
        await AcknowledgeAsync("employee", "Code of Conduct", "2.0", new DateTime(2026, 1, 6, 10, 30, 0, DateTimeKind.Utc));
        await AcknowledgeAsync("manager", "Employee Handbook 2026", "2.1", new DateTime(2026, 1, 5, 9, 15, 0, DateTimeKind.Utc));
        await AcknowledgeAsync("manager", "Code of Conduct", "2.0", new DateTime(2026, 1, 5, 9, 17, 0, DateTimeKind.Utc));
        await AcknowledgeAsync("hr", "Code of Conduct", "2.0", new DateTime(2026, 1, 4, 11, 0, 0, DateTimeKind.Utc));

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedLetterRequestAsync(CancellationToken cancellationToken)
    {
        if (await _db.Set<LetterRequest>().AsNoTracking().AnyAsync(cancellationToken)) return;

        var employee = await _db.UserAccounts.AsNoTracking()
            .Where(u => u.UserName == "employee" && u.EmployeeId != null)
            .Select(u => new { EmployeeId = u.EmployeeId!.Value, u.Id })
            .FirstOrDefaultAsync(cancellationToken);
        if (employee is null) return;

        var letter = new LetterRequest
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.EmployeeId,
            LetterType = "SalaryCertificate",
            Purpose = "Home loan processing with HDFC Bank",
            Notes = "Requested for the financial year 2026-27 application.",
            Status = LetterRequestStatus.Pending,
            CreatedAt = DateTime.UtcNow.AddDays(-3)
        };

        // Attach the approval workflow when the Approvals module is available; otherwise the
        // example stays a plain pending request.
        var approvals = _serviceProvider.GetService<IApprovalService>();
        if (approvals is not null)
        {
            var task = await approvals.StartAsync(new ApprovalStartRequest(
                "Letter", letter.Id, $"Salary certificate request — Priya Nair",
                employee.Id, employee.EmployeeId, "Priya Nair"), cancellationToken);
            letter.ApprovalTaskId = task.Id;
        }

        _db.Set<LetterRequest>().Add(letter);
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Seeded example letter request {LetterId} (approval task: {TaskId}).",
            letter.Id, letter.ApprovalTaskId?.ToString() ?? "none");
    }

    private static CompanyPolicy NewPolicy(string title, string category, string version, string summary,
        string body, bool requiresAcknowledgement, DateOnly effectiveFrom, DateTime publishedAt) => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        Category = category,
        Version = version,
        Summary = summary,
        Body = body,
        RequiresAcknowledgement = requiresAcknowledgement,
        EffectiveFrom = effectiveFrom,
        PublishedAt = publishedAt,
        IsActive = true
    };

    /// <summary>Small valid PDF placeholder so seeded documents are downloadable end-to-end.</summary>
    private static byte[] BuildPlaceholderPdf(string documentType, string employeeNumber)
    {
        var label = $"{documentType} — {employeeNumber} (seeded sample record)";
        return QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(QuestPDF.Helpers.PageSizes.A4);
                page.Margin(48);
                page.Content().AlignCenter().AlignMiddle().Text(label).FontSize(14);
            });
        }).GeneratePdf();
    }
}
