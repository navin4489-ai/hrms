using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.Registration;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Cases;

namespace Wintech.Hrms.Api.Data.Seed;

/// <summary>
/// Idempotent helpdesk seed (FR-EMP-111/115, FR-ADM-120): case categories with subcategories
/// (one confidential), knowledge-base articles and demo cases across statuses/priorities with
/// employee replies, an internal note and a confidential restricted case (UAT-009).
/// </summary>
public sealed class CaseSeedData : IDataSeeder
{
    private readonly AppDbContext _db;
    private readonly ILogger<CaseSeedData> _logger;

    public CaseSeedData(AppDbContext db, ILogger<CaseSeedData> logger)
    {
        _db = db;
        _logger = logger;
    }

    public int Order => 140;

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var categoriesSeeded = await SeedCategoriesAsync(cancellationToken);
        await SeedKnowledgeBaseAsync(cancellationToken);
        await SeedDemoCasesAsync(cancellationToken);

        if (categoriesSeeded)
            _logger.LogInformation("Seeded helpdesk categories, knowledge base and demo cases.");
    }

    private static Guid Cat(int n) => Guid.Parse($"00000000-0000-0000-0001-{n:D12}");
    private static Guid Sub(int n) => Guid.Parse($"00000000-0000-0000-0002-{n:D12}");
    private static Guid Art(int n) => Guid.Parse($"00000000-0000-0000-0003-{n:D12}");
    private static Guid Cs(int n) => Guid.Parse($"00000000-0000-0000-0004-{n:D12}");
    private static Guid Msg(int n) => Guid.Parse($"00000000-0000-0000-0005-{n:D12}");

    private async Task<bool> SeedCategoriesAsync(CancellationToken cancellationToken)
    {
        if (await _db.Set<CaseCategory>().AnyAsync(cancellationToken)) return false;

        var definitions = new (int N, string Name, bool Confidential, string[] Subcategories)[]
        {
            (1, "Payroll", false, ["Salary slip", "Tax deduction", "Reimbursement"]),
            (2, "Leave", false, ["Casual leave", "Sick leave", "Leave balance"]),
            (3, "Attendance", false, ["Missing punch", "Late arrival", "Attendance correction"]),
            (4, "Benefits", true, ["Health insurance", "Wellness benefits", "Claims"]),
            (5, "Policy", false, ["HR policy question", "Code of conduct"]),
            (6, "Onboarding", false, ["Joining formalities", "Documents", "Buddy assignment"]),
            (7, "Workplace", false, ["Facility request", "Access card", "Workplace safety"]),
            (8, "IT", false, ["Laptop & accessories", "Software access", "VPN & MFA"]),
            (9, "General HR", false, ["Employment letter", "Employee query"])
        };

        var sortOrder = 0;
        foreach (var (n, name, confidential, subcategories) in definitions)
        {
            var category = new CaseCategory
            {
                Id = Cat(n),
                Name = name,
                Confidential = confidential,
                IsActive = true,
                SortOrder = sortOrder++
            };
            var subIndex = 0;
            foreach (var subName in subcategories)
            {
                category.Subcategories.Add(new CaseSubcategory
                {
                    Id = Sub(n * 10 + ++subIndex),
                    Name = subName,
                    IsActive = true
                });
            }
            _db.Set<CaseCategory>().Add(category);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task SeedKnowledgeBaseAsync(CancellationToken cancellationToken)
    {
        if (await _db.Set<CaseKnowledgeArticle>().AnyAsync(cancellationToken)) return;

        var now = DateTime.UtcNow;
        var articles = new[]
        {
            new CaseKnowledgeArticle
            {
                Id = Art(1), Title = "Understanding your payslip",
                Summary = "A line-by-line guide to earnings, deductions, LOP adjustments and tax columns on your monthly payslip.",
                Body = "Your payslip lists earnings (basic, HRA, special allowance), deductions (PF, TDS, LOP) and net pay. "
                     + "Loss of pay (LOP) days reduce the basic proportionally. Tax columns reflect the regime selected in the "
                     + "declaration window. Contact the Payroll queue if a figure still looks wrong.",
                Category = "Payroll", Tags = "salary,payslip,tax,lop", HelpfulCount = 42, Version = "2.1",
                UpdatedAt = now.AddDays(-20), ExpiresOn = null, IsActive = true
            },
            new CaseKnowledgeArticle
            {
                Id = Art(2), Title = "How to apply for casual leave",
                Summary = "Apply for casual or sick leave from the Leave module; your line manager approves within the SLA.",
                Body = "Open Leave -> New request, choose the leave type and dates, add a note for your manager and submit. "
                     + "Casual leave requires a balance; sick leave beyond three consecutive days needs a medical certificate. "
                     + "You can track approval status from My Requests.",
                Category = "Leave", Tags = "leave,casual,sick,balance,approval", HelpfulCount = 65, Version = "1.4",
                UpdatedAt = now.AddDays(-15), ExpiresOn = null, IsActive = true
            },
            new CaseKnowledgeArticle
            {
                Id = Art(3), Title = "Correcting a missing punch",
                Summary = "Raise an attendance correction when check-in or check-out is missing; your manager approves it.",
                Body = "Go to Attendance, pick the date with the MissingPunch exception and choose Request correction. "
                     + "Add the actual times and an optional note. Corrections route to your line manager and reflect after approval.",
                Category = "Attendance", Tags = "attendance,punch,correction,missing", HelpfulCount = 31, Version = "1.2",
                UpdatedAt = now.AddDays(-10), ExpiresOn = null, IsActive = true
            },
            new CaseKnowledgeArticle
            {
                Id = Art(4), Title = "Enrolling in health insurance",
                Summary = "Annual open enrollment for employee and family floater cover, claim process and nominee updates.",
                Body = "Open enrollment runs once a year; you can add spouse, children and dependent parents. "
                     + "Premiums are deducted from salary. Claims need the discharge summary and bills; cashless treatment is "
                     + "available at network hospitals. Coverage changes apply from the next policy period.",
                Category = "Benefits", Tags = "insurance,health,claims,nominee,enrollment", HelpfulCount = 27, Version = "3.0",
                UpdatedAt = now.AddDays(-5), ExpiresOn = null, IsActive = true
            },
            new CaseKnowledgeArticle
            {
                Id = Art(5), Title = "Your first week at Wintech",
                Summary = "Accounts, buddy program, mandatory policies and the week-one checklist for new joiners.",
                Body = "Day one: IT accounts, laptop and access card. Complete the compliance modules in the first week, "
                     + "meet your buddy and set goals with your manager in the first one-on-one. "
                     + "All documents belong in the Employee Documents section.",
                Category = "Onboarding", Tags = "onboarding,joiner,checklist,buddy", HelpfulCount = 18, Version = "1.0",
                UpdatedAt = now.AddDays(-30), ExpiresOn = null, IsActive = true
            },
            new CaseKnowledgeArticle
            {
                Id = Art(6), Title = "Setting up VPN and MFA",
                Summary = "Install the VPN profile, enrol authenticator MFA and troubleshoot common login failures.",
                Body = "Download the VPN profile from the IT portal, sign in with your work account and enrol MFA in "
                     + "Security settings. If a code does not arrive, use a backup code or raise an IT helpdesk case "
                     + "with your employee number and device name.",
                Category = "IT", Tags = "vpn,mfa,security,login,access", HelpfulCount = 54, Version = "2.0",
                UpdatedAt = now.AddDays(-8), ExpiresOn = null, IsActive = true
            }
        };

        _db.Set<CaseKnowledgeArticle>().AddRange(articles);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedDemoCasesAsync(CancellationToken cancellationToken)
    {
        if (await _db.Set<HelpdeskCase>().AnyAsync(cancellationToken)) return;

        var now = DateTime.UtcNow;
        var year = now.Year;
        string Ticket(int sequence) => $"CS-{year:D4}-{sequence:D4}";

        Guid? EmployeeId(string number) =>
            _db.Employees.AsNoTracking()
                .Where(e => e.EmployeeNumber == number)
                .Select(e => (Guid?)e.Id)
                .FirstOrDefault();

        var priya = EmployeeId("EMP006");   // employee (Priya Nair)
        var rohit = EmployeeId("EMP007");   // employee2 (Rohit Verma)
        var sandeep = EmployeeId("EMP004"); // hrexec (Sandeep Rao)
        var vikram = EmployeeId("EMP009");  // Vikram Shah
        if (priya is null || rohit is null || sandeep is null || vikram is null) return; // core seed missing

        // ---- 1. Payroll case: employee replied, HR answered + left an internal note (FR-ADM-122)
        var case1 = new HelpdeskCase
        {
            Id = Cs(1), TicketNumber = Ticket(1),
            CategoryId = Cat(1), SubcategoryId = Sub(11),
            Subject = "Salary slip for last month shows incorrect LOP",
            Description = "My payslip shows two loss-of-pay days although my approved leave covers those dates.",
            Priority = CasePriority.Medium, Status = CaseStatus.WaitingOnHR,
            Queue = "Payroll", Confidential = false,
            EmployeeId = priya.Value,
            CreatedAt = now.AddDays(-1.2), UpdatedAt = now.AddHours(-5),
            SlaDueAt = now.AddDays(-1.2).AddHours(24),
            FirstResponseAt = now.AddHours(-6)
        };
        case1.Messages.Add(new CaseMessage
        {
            Id = Msg(1), CaseId = case1.Id, SenderUserId = null, SenderName = "Priya Nair", SenderRole = "Employee",
            Visibility = CaseMessageVisibility.Employee,
            Body = "Attaching the approved leave screenshot for reference.", CreatedAt = now.AddDays(-1.2),
            AttachmentUrl = "/api/v1/cases/attachments/leave-approval.png"
        });
        case1.Messages.Add(new CaseMessage
        {
            Id = Msg(2), CaseId = case1.Id, SenderUserId = null, SenderName = "Meera Iyer", SenderRole = "HR",
            Visibility = CaseMessageVisibility.Internal,
            Body = "Leave records look correct in the system; payroll may have used a stale export. Chasing payroll ops.",
            CreatedAt = now.AddHours(-7)
        });
        case1.Messages.Add(new CaseMessage
        {
            Id = Msg(3), CaseId = case1.Id, SenderUserId = null, SenderName = "Meera Iyer", SenderRole = "HR",
            Visibility = CaseMessageVisibility.Employee,
            Body = "Thanks for the details — we are verifying with payroll and will revert within a day.",
            CreatedAt = now.AddHours(-6)
        });

        // ---- 2. Attendance case: resolved and rated (FR-EMP-113)
        var case2 = new HelpdeskCase
        {
            Id = Cs(2), TicketNumber = Ticket(2),
            CategoryId = Cat(3), SubcategoryId = Sub(31),
            Subject = "Missing check-out on Monday",
            Description = "I forgot to check out on Monday; please correct the day to 8.5 hours.",
            Priority = CasePriority.Low, Status = CaseStatus.Resolved,
            Queue = "HR", Confidential = false,
            EmployeeId = rohit.Value,
            CreatedAt = now.AddDays(-3), UpdatedAt = now.AddDays(-2),
            SlaDueAt = now.AddDays(-3).AddHours(48),
            FirstResponseAt = now.AddDays(-2.8),
            ResolvedAt = now.AddDays(-2),
            ResolutionCode = "CorrectionApplied",
            Rating = 4, RatingComment = "Resolved quickly, thanks!"
        };
        case2.Messages.Add(new CaseMessage
        {
            Id = Msg(4), CaseId = case2.Id, SenderUserId = null, SenderName = "Rohit Verma", SenderRole = "Employee",
            Visibility = CaseMessageVisibility.Employee,
            Body = "Attending a client call past checkout time — forgot to punch out.", CreatedAt = now.AddDays(-3)
        });
        case2.Messages.Add(new CaseMessage
        {
            Id = Msg(5), CaseId = case2.Id, SenderUserId = null, SenderName = "Sandeep Rao", SenderRole = "HR",
            Visibility = CaseMessageVisibility.Employee,
            Body = "Attendance corrected to 8.5 hours for that day. Please verify.",
            CreatedAt = now.AddDays(-2.8)
        });

        // ---- 3. Confidential Benefits case in the restricted queue (FR-EMP-114, UAT-009)
        var case3 = new HelpdeskCase
        {
            Id = Cs(3), TicketNumber = Ticket(3),
            CategoryId = Cat(4), SubcategoryId = Sub(41),
            Subject = "Medical insurance claim for dependent parent",
            Description = "Need help with the cashless claim process for my father's hospitalisation.",
            Priority = CasePriority.High, Status = CaseStatus.Open,
            Queue = "Restricted", Confidential = true,
            EmployeeId = priya.Value,
            CreatedAt = now.AddDays(-0.5), UpdatedAt = now.AddHours(-1),
            SlaDueAt = now.AddDays(-0.5).AddHours(8)
        };
        case3.Messages.Add(new CaseMessage
        {
            Id = Msg(6), CaseId = case3.Id, SenderUserId = null, SenderName = "Meera Iyer", SenderRole = "HR",
            Visibility = CaseMessageVisibility.Internal,
            Body = "Restricted case — keep medical details within the HR-Benefits queue only. Share claim form link privately.",
            CreatedAt = now.AddHours(-1)
        });

        // ---- 4. Onboarding case: closed (FR-ADM-124 history kept)
        var case4 = new HelpdeskCase
        {
            Id = Cs(4), TicketNumber = Ticket(4),
            CategoryId = Cat(6), SubcategoryId = Sub(62),
            Subject = "Pending onboarding documents",
            Description = "Which documents are still pending from my side?",
            Priority = CasePriority.Medium, Status = CaseStatus.Closed,
            Queue = "HR", Confidential = false,
            EmployeeId = sandeep.Value,
            CreatedAt = now.AddDays(-8), UpdatedAt = now.AddDays(-6),
            SlaDueAt = now.AddDays(-8).AddHours(24),
            FirstResponseAt = now.AddDays(-7.9),
            ResolvedAt = now.AddDays(-6.5),
            ClosedAt = now.AddDays(-6),
            ResolutionCode = "InformationProvided"
        };
        case4.Messages.Add(new CaseMessage
        {
            Id = Msg(7), CaseId = case4.Id, SenderUserId = null, SenderName = "Sandeep Rao", SenderRole = "HR",
            Visibility = CaseMessageVisibility.Employee,
            Body = "Only the address proof is pending; please upload it in Employee Documents.",
            CreatedAt = now.AddDays(-7.9)
        });

        // ---- 5. IT case: reopened by the employee within the window (FR-EMP-113)
        var case5 = new HelpdeskCase
        {
            Id = Cs(5), TicketNumber = Ticket(5),
            CategoryId = Cat(8), SubcategoryId = Sub(83),
            Subject = "VPN disconnects after MFA enrolment",
            Description = "VPN worked before enrolling MFA; now it drops every few minutes.",
            Priority = CasePriority.Urgent, Status = CaseStatus.Reopened,
            Queue = "IT", Confidential = false,
            EmployeeId = vikram.Value,
            CreatedAt = now.AddDays(-4), UpdatedAt = now.AddHours(-2),
            SlaDueAt = now.AddHours(-2).AddHours(4),
            FirstResponseAt = now.AddDays(-3.95),
            ResolvedAt = now.AddDays(-1),
            ResolutionCode = "ConfigurationFixed",
            ReopenedAt = now.AddHours(-2),
            ReopenCount = 1
        };
        case5.Messages.Add(new CaseMessage
        {
            Id = Msg(8), CaseId = case5.Id, SenderUserId = null, SenderName = "Vikram Shah", SenderRole = "Employee",
            Visibility = CaseMessageVisibility.Employee,
            Body = "Reopening — the disconnects started again this morning after the profile re-sync.",
            CreatedAt = now.AddHours(-2)
        });

        _db.Set<HelpdeskCase>().AddRange(case1, case2, case3, case4, case5);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
