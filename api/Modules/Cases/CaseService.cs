using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Wintech.Hrms.Api.Common.Auditing;
using Wintech.Hrms.Api.Common.CurrentUser;
using Wintech.Hrms.Api.Common.ErrorHandling;
using Wintech.Hrms.Api.Common.Notifications;
using Wintech.Hrms.Api.Common.Paging;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Cases;
using Wintech.Hrms.Api.Domain.Identity;
using Wintech.Hrms.Api.Security;

namespace Wintech.Hrms.Api.Modules.Cases;

/// <summary>
/// HR helpdesk / employee CRM core (FR-EMP-110..115, FR-ADM-120..125).
/// Confidential cases are 404 for everyone outside the restricted queue (FR-EMP-114, UAT-009);
/// internal notes are never shown to employees (FR-ADM-122).
/// </summary>
public sealed class HelpdeskCaseService : ICaseService
{
    private const int DefaultReopenWindowDays = 3;

    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;
    private readonly INotificationPublisher _notifications;
    private readonly IClock _clock;
    private readonly IConfiguration _configuration;

    public HelpdeskCaseService(AppDbContext db, ICurrentUser currentUser, IAuditService audit,
        IEnumerable<INotificationPublisher> publishers, IClock clock, IConfiguration configuration)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        // The no-op fallback is registered twice (Program.cs + auto-discovery re-registration), which
        // can make plain INotificationPublisher resolution return the no-op — pick the real pipeline.
        _notifications = publishers.FirstOrDefault(p => p is not NoopNotificationPublisher)
            ?? publishers.LastOrDefault()
            ?? throw new InvalidOperationException("No notification publisher is registered.");
        _clock = clock;
        _configuration = configuration;
    }

    private bool CanManage => _currentUser.HasPermission(Permissions.CaseManage);
    private bool CanViewRestricted => _currentUser.HasPermission(Permissions.CaseViewRestricted);

    // ---------------------------------------------------------------- categories

    public async Task<IReadOnlyList<CaseCategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var categories = await _db.Set<CaseCategory>().AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new
            {
                c.Id, c.Name, c.Confidential,
                Subcategories = c.Subcategories
                    .Where(s => s.IsActive)
                    .OrderBy(s => s.Name)
                    .Select(s => new { s.Id, s.Name })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        return categories.Select(c => new CaseCategoryDto(c.Id, c.Name, c.Confidential,
            c.Subcategories.Select(s => new CaseSubcategoryDto(s.Id, s.Name)).ToList())).ToList();
    }

    // ---------------------------------------------------------------- list

    public async Task<PagedResult<CaseListItem>> ListAsync(PageRequest page, CaseStatus? status, string? category,
        CasePriority? priority, bool mine, string? queue, CancellationToken cancellationToken = default)
    {
        var query = VisibleCases();

        if (status is { } statusFilter) query = query.Where(c => c.Status == statusFilter);
        if (priority is { } priorityFilter) query = query.Where(c => c.Priority == priorityFilter);

        if (!string.IsNullOrWhiteSpace(category))
        {
            var term = category.Trim();
            if (Guid.TryParse(term, out var categoryId))
            {
                query = query.Where(c => c.CategoryId == categoryId);
            }
            else
            {
                var lowered = term.ToLowerInvariant();
                query = query.Where(c => c.Category != null && c.Category.Name.ToLower() == lowered);
            }
        }

        if (!string.IsNullOrWhiteSpace(queue))
        {
            // Queues are an agent view (contract §15): a plain employee may not browse them.
            if (!CanManage) throw new ForbiddenException("Browsing queues requires case management permission.");
            var queueTerm = queue.Trim().ToLowerInvariant();
            query = query.Where(c => c.Queue.ToLower() == queueTerm);
        }

        var userId = _currentUser.UserId;
        var employeeId = _currentUser.EmployeeId;
        if (mine || !CanManage)
        {
            query = query.Where(c => c.EmployeeId == employeeId || c.AssigneeUserId == userId);
        }

        return await query
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new CaseListItem
            {
                Id = c.Id,
                TicketNumber = c.TicketNumber,
                Subject = c.Subject,
                Category = c.Category != null ? c.Category.Name : null,
                Subcategory = c.Subcategory != null ? c.Subcategory.Name : null,
                Priority = c.Priority,
                Status = c.Status,
                Queue = c.Queue,
                Confidential = c.Confidential,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt,
                SlaDueAt = c.SlaDueAt,
                FirstResponseAt = c.FirstResponseAt,
                ResolvedAt = c.ResolvedAt,
                AssigneeName = c.Assignee != null ? c.Assignee.DisplayName : null,
                EmployeeName = c.Employee != null ? c.Employee.DisplayName : null,
                Rating = c.Rating
            })
            .ToPagedAsync(page, item => item, cancellationToken);
    }

    // ---------------------------------------------------------------- create

    public async Task<CaseCreatedResult> CreateAsync(CreateCaseRequest request, CancellationToken cancellationToken = default)
    {
        _currentUser.EnsureActive();
        var employeeId = _currentUser.EmployeeId
            ?? throw new ForbiddenException("Your profile is not linked to an employee record.");

        var category = await _db.Set<CaseCategory>().AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.CategoryId && c.IsActive, cancellationToken)
            ?? throw new NotFoundException("The selected category was not found.");

        Guid? subcategoryId = null;
        if (request.SubcategoryId is { } subcategoryIdValue)
        {
            var belongs = await _db.Set<CaseSubcategory>().AsNoTracking()
                .AnyAsync(s => s.Id == subcategoryIdValue && s.CategoryId == category.Id && s.IsActive,
                    cancellationToken);
            if (!belongs) throw new RequestValidationException("The selected subcategory does not belong to the category.",
                new() { ["subcategoryId"] = ["The selected subcategory does not belong to the category."] });
            subcategoryId = subcategoryIdValue;
        }

        var now = _clock.UtcNow;
        var ticketNumber = await NextTicketNumberAsync(now, cancellationToken);
        var helpdeskCase = new HelpdeskCase
        {
            Id = Guid.NewGuid(),
            TicketNumber = ticketNumber,
            CategoryId = category.Id,
            SubcategoryId = subcategoryId,
            Subject = request.Subject.Trim(),
            Description = request.Description.Trim(),
            Priority = request.Urgency,
            Status = CaseStatus.Open,
            Queue = QueueFor(category),
            Confidential = category.Confidential,
            EmployeeId = employeeId,
            CreatedAt = now,
            UpdatedAt = now,
            SlaDueAt = now.AddHours(SlaHours(request.Urgency))
        };

        if (!string.IsNullOrWhiteSpace(request.AttachmentUrl))
        {
            helpdeskCase.Messages.Add(new CaseMessage
            {
                Id = Guid.NewGuid(),
                CaseId = helpdeskCase.Id,
                SenderUserId = _currentUser.UserId,
                SenderName = _currentUser.DisplayName,
                SenderRole = "Employee",
                Visibility = CaseMessageVisibility.Employee,
                Body = "Attached supporting evidence.",
                AttachmentUrl = request.AttachmentUrl.Trim(),
                CreatedAt = now
            });
        }

        _db.Set<HelpdeskCase>().Add(helpdeskCase);
        _audit.Add("case.create", nameof(HelpdeskCase), helpdeskCase.Id.ToString(), "Success",
            new { helpdeskCase.TicketNumber, category = category.Name, helpdeskCase.Priority, helpdeskCase.Queue });
        await _db.SaveChangesAsync(cancellationToken);

        await NotifyCaseAgentsAsync(helpdeskCase, "CaseCreated",
            $"New helpdesk case {ticketNumber}",
            $"A new {category.Name} case ({ticketNumber}) has been raised: {helpdeskCase.Subject}.",
            cancellationToken);

        return new CaseCreatedResult(helpdeskCase.Id, ticketNumber, helpdeskCase.Status, helpdeskCase.SlaDueAt);
    }

    // ---------------------------------------------------------------- detail

    public async Task<CaseDetailDto> GetDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var helpdeskCase = await BuildDetailQuery(id, includeInternal: CanManage, cancellationToken: cancellationToken)
            ?? throw CaseNotFound();

        return helpdeskCase;
    }

    // ---------------------------------------------------------------- messages & notes

    public async Task<CaseMessageDto> AddMessageAsync(Guid id, AddCaseMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        _currentUser.EnsureActive();
        var helpdeskCase = await RequireAccessibleCaseAsync(id, cancellationToken, track: true);
        if (helpdeskCase.Status == CaseStatus.Closed)
            throw new ConflictException("This case is closed and can no longer be replied to.");

        var isAgent = CanManage;
        var isOwner = helpdeskCase.EmployeeId == _currentUser.EmployeeId;
        if (!isAgent && !isOwner) throw CaseNotFound();
        if (!isAgent && helpdeskCase.Status == CaseStatus.Resolved)
            throw new ConflictException("This case is resolved. Reopen it to continue the conversation.");

        var now = _clock.UtcNow;
        var message = new CaseMessage
        {
            Id = Guid.NewGuid(),
            CaseId = helpdeskCase.Id,
            SenderUserId = _currentUser.UserId,
            SenderName = _currentUser.DisplayName,
            SenderRole = isAgent ? "HR" : "Employee",
            Visibility = CaseMessageVisibility.Employee,
            Body = request.Body.Trim(),
            AttachmentUrl = string.IsNullOrWhiteSpace(request.AttachmentUrl) ? null : request.AttachmentUrl.Trim(),
            CreatedAt = now
        };
        _db.Set<CaseMessage>().Add(message);

        helpdeskCase.UpdatedAt = now;
        if (!isAgent && helpdeskCase.Status == CaseStatus.WaitingOnEmployee)
            helpdeskCase.Status = CaseStatus.WaitingOnHR; // the employee supplied the requested information
        if (isAgent && message.Visibility == CaseMessageVisibility.Employee)
            helpdeskCase.FirstResponseAt ??= now;

        _audit.Add("case.message", nameof(HelpdeskCase), helpdeskCase.Id.ToString(), "Success",
            new { helpdeskCase.TicketNumber, senderRole = message.SenderRole });
        await _db.SaveChangesAsync(cancellationToken);

        if (isAgent)
        {
            await NotifyEmployeeAsync(helpdeskCase, "CaseReplied",
                $"Update on your case {helpdeskCase.TicketNumber}",
                "HR has replied to your helpdesk case. Open the app to read the update.",
                cancellationToken);
        }
        else
        {
            await NotifyCaseAgentsAsync(helpdeskCase, "CaseReplied",
                $"New reply on case {helpdeskCase.TicketNumber}",
                $"{_currentUser.DisplayName} replied to helpdesk case {helpdeskCase.TicketNumber}.",
                cancellationToken);
        }

        return MapMessage(message);
    }

    public async Task<CaseMessageDto> AddNoteAsync(Guid id, AddCaseNoteRequest request,
        CancellationToken cancellationToken = default)
    {
        var helpdeskCase = await RequireAccessibleCaseAsync(id, cancellationToken, track: true);
        var now = _clock.UtcNow;
        var message = new CaseMessage
        {
            Id = Guid.NewGuid(),
            CaseId = helpdeskCase.Id,
            SenderUserId = _currentUser.UserId,
            SenderName = _currentUser.DisplayName,
            SenderRole = "HR",
            Visibility = CaseMessageVisibility.Internal, // FR-ADM-122: internal-only
            Body = request.Body.Trim(),
            CreatedAt = now
        };
        _db.Set<CaseMessage>().Add(message);
        helpdeskCase.UpdatedAt = now;

        _audit.Add("case.note", nameof(HelpdeskCase), helpdeskCase.Id.ToString(), "Success",
            new { helpdeskCase.TicketNumber });
        await _db.SaveChangesAsync(cancellationToken);

        return MapMessage(message);
    }

    // ---------------------------------------------------------------- transitions

    public async Task<CaseDetailDto> TransitionAsync(Guid id, CaseTransitionRequest request,
        CancellationToken cancellationToken = default)
    {
        _currentUser.EnsureActive();
        var helpdeskCase = await RequireAccessibleCaseAsync(id, cancellationToken, track: true);
        var now = _clock.UtcNow;
        var isOwner = helpdeskCase.EmployeeId == _currentUser.EmployeeId;

        switch (request.Action)
        {
            case CaseTransitionAction.Resolve:
                if (!CanManage) throw new ForbiddenException("Only HR case owners can resolve cases.");
                if (helpdeskCase.Status is CaseStatus.Resolved or CaseStatus.Closed)
                    throw new ConflictException("This case is already resolved or closed.");
                helpdeskCase.Status = CaseStatus.Resolved;
                helpdeskCase.ResolvedAt = now;
                helpdeskCase.ResolutionCode = string.IsNullOrWhiteSpace(request.ResolutionCode)
                    ? "Resolved"
                    : request.ResolutionCode.Trim();
                break;

            case CaseTransitionAction.Close:
                if (!CanManage) throw new ForbiddenException("Only HR case owners can close cases.");
                if (helpdeskCase.Status == CaseStatus.Closed)
                    throw new ConflictException("This case is already closed.");
                helpdeskCase.Status = CaseStatus.Closed;
                helpdeskCase.ClosedAt = now;
                helpdeskCase.ResolvedAt ??= now;
                helpdeskCase.ResolutionCode ??= string.IsNullOrWhiteSpace(request.ResolutionCode)
                    ? "Closed"
                    : request.ResolutionCode.Trim();
                break;

            case CaseTransitionAction.Reopen:
                if (!CanManage && !isOwner)
                    throw new ForbiddenException("Only the case owner or HR can reopen a case.");
                if (helpdeskCase.Status is not (CaseStatus.Resolved or CaseStatus.Closed))
                    throw new ConflictException("Only resolved or closed cases can be reopened.");
                var reference = helpdeskCase.ResolvedAt ?? helpdeskCase.ClosedAt ?? helpdeskCase.CreatedAt;
                var windowDays = _configuration.GetValue("Helpdesk:ReopenWindowDays", DefaultReopenWindowDays);
                if (now > reference.AddDays(windowDays))
                    throw new BusinessRuleException(
                        $"Cases can be reopened within {windowDays} days of resolution. Please raise a new case instead.",
                        new() { ["action"] = [$"The reopen window of {windowDays} days has expired."] });
                helpdeskCase.Status = CaseStatus.Reopened;
                helpdeskCase.ReopenedAt = now;
                helpdeskCase.ReopenCount += 1;
                helpdeskCase.SlaDueAt = now.AddHours(SlaHours(helpdeskCase.Priority));
                break;

            default:
                throw new RequestValidationException("The transition action is not supported.",
                    new() { ["action"] = ["Action must be Resolve, Close or Reopen."] });
        }

        if (!string.IsNullOrWhiteSpace(request.Comment))
        {
            _db.Set<CaseMessage>().Add(new CaseMessage
            {
                Id = Guid.NewGuid(),
                CaseId = helpdeskCase.Id,
                SenderUserId = _currentUser.UserId,
                SenderName = _currentUser.DisplayName,
                SenderRole = CanManage ? "HR" : "Employee",
                Visibility = CaseMessageVisibility.Employee,
                Body = request.Comment.Trim(),
                CreatedAt = now
            });
        }

        helpdeskCase.UpdatedAt = now;
        _audit.Add("case.transition", nameof(HelpdeskCase), helpdeskCase.Id.ToString(), "Success",
            new { helpdeskCase.TicketNumber, action = request.Action.ToString(), helpdeskCase.Status });
        await _db.SaveChangesAsync(cancellationToken);

        switch (request.Action)
        {
            case CaseTransitionAction.Resolve:
                await NotifyEmployeeAsync(helpdeskCase, "CaseResolved",
                    $"Your case {helpdeskCase.TicketNumber} has been resolved",
                    "Your helpdesk case has been resolved. Please rate the resolution in the app.",
                    cancellationToken);
                break;
            case CaseTransitionAction.Close:
                await NotifyEmployeeAsync(helpdeskCase, "CaseClosed",
                    $"Your case {helpdeskCase.TicketNumber} has been closed",
                    "Your helpdesk case has been closed.",
                    cancellationToken);
                break;
            case CaseTransitionAction.Reopen:
                await NotifyCaseAgentsAsync(helpdeskCase, "CaseReopened",
                    $"Case {helpdeskCase.TicketNumber} reopened",
                    $"Helpdesk case {helpdeskCase.TicketNumber} has been reopened.",
                    cancellationToken);
                break;
        }

        return (await BuildDetailQuery(helpdeskCase.Id, includeInternal: CanManage, cancellationToken: cancellationToken))!;
    }

    // ---------------------------------------------------------------- rating

    public async Task<CaseDetailDto> RateAsync(Guid id, RateCaseRequest request, CancellationToken cancellationToken = default)
    {
        _currentUser.EnsureActive();
        var helpdeskCase = await RequireAccessibleCaseAsync(id, cancellationToken, track: true);
        if (helpdeskCase.EmployeeId != _currentUser.EmployeeId)
            throw CaseNotFound(); // only the case owner rates the resolution (FR-EMP-113)
        if (helpdeskCase.Status is not (CaseStatus.Resolved or CaseStatus.Closed) || helpdeskCase.ResolvedAt is null)
            throw new BusinessRuleException("A case can only be rated after it has been resolved.",
                new() { ["rating"] = ["The case has not been resolved yet."] });
        if (helpdeskCase.Rating is not null)
            throw new ConflictException("This case has already been rated.");

        helpdeskCase.Rating = request.Rating;
        helpdeskCase.RatingComment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
        helpdeskCase.UpdatedAt = _clock.UtcNow;

        _audit.Add("case.rate", nameof(HelpdeskCase), helpdeskCase.Id.ToString(), "Success",
            new { helpdeskCase.TicketNumber, request.Rating });
        await _db.SaveChangesAsync(cancellationToken);

        return (await BuildDetailQuery(helpdeskCase.Id, includeInternal: CanManage, cancellationToken: cancellationToken))!;
    }

    // ---------------------------------------------------------------- knowledge base (FR-EMP-115)

    public async Task<IReadOnlyList<KnowledgeArticleListItem>> SearchKnowledgeBaseAsync(string? q, string? category,
        CancellationToken cancellationToken = default)
    {
        var today = _clock.Today;
        var query = _db.Set<CaseKnowledgeArticle>().AsNoTracking()
            .Where(a => a.IsActive && (a.ExpiresOn == null || a.ExpiresOn >= today));

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLowerInvariant();
            query = query.Where(a =>
                a.Title.ToLower().Contains(term) ||
                a.Summary.ToLower().Contains(term) ||
                a.Tags.ToLower().Contains(term) ||
                a.Body.ToLower().Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            var categoryTerm = category.Trim().ToLowerInvariant();
            query = query.Where(a => a.Category.ToLower() == categoryTerm);
        }

        var articles = await query
            .OrderByDescending(a => a.UpdatedAt)
            .Select(a => new
            {
                a.Id, a.Title, a.Summary, a.Category, a.Tags, a.HelpfulCount, a.UpdatedAt
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return articles
            .Select(a => new KnowledgeArticleListItem(a.Id, a.Title, a.Summary, a.Category,
                a.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                a.HelpfulCount, a.UpdatedAt))
            .ToList();
    }

    public async Task<KnowledgeArticleDetail> GetKnowledgeArticleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var today = _clock.Today;
        var article = await _db.Set<CaseKnowledgeArticle>().AsNoTracking()
            .Where(a => a.Id == id && a.IsActive && (a.ExpiresOn == null || a.ExpiresOn >= today))
            .Select(a => new KnowledgeArticleDetail(a.Id, a.Title, a.Body, a.Category, a.Version, a.ExpiresOn))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("The requested article was not found.");

        return article;
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Confidential cases are invisible outside the restricted queue (FR-EMP-114 / UAT-009).</summary>
    private IQueryable<HelpdeskCase> VisibleCases()
    {
        var userId = _currentUser.UserId;
        var employeeId = _currentUser.EmployeeId;
        var query = _db.Set<HelpdeskCase>().AsNoTracking();

        if (CanViewRestricted) return query;
        if (CanManage)
            return query.Where(c => !c.Confidential || c.EmployeeId == employeeId || c.AssigneeUserId == userId);
        return query.Where(c => c.EmployeeId == employeeId || c.AssigneeUserId == userId);
    }

    private async Task<HelpdeskCase> RequireAccessibleCaseAsync(Guid id, CancellationToken cancellationToken, bool track = false)
    {
        IQueryable<HelpdeskCase> query = track
            ? _db.Set<HelpdeskCase>().Include(c => c.Category).Include(c => c.Subcategory)
            : _db.Set<HelpdeskCase>().AsNoTracking();

        if (!CanViewRestricted)
        {
            var userId = _currentUser.UserId;
            var employeeId = _currentUser.EmployeeId;
            query = query.Where(c => !c.Confidential || c.EmployeeId == employeeId || c.AssigneeUserId == userId);
        }

        var helpdeskCase = await query
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw CaseNotFound();

        if (!CanManage && helpdeskCase.EmployeeId != _currentUser.EmployeeId && helpdeskCase.AssigneeUserId != _currentUser.UserId)
            throw CaseNotFound();

        return helpdeskCase;
    }

    private static NotFoundException CaseNotFound() => new("The requested case was not found.");

    private async Task<CaseDetailDto?> BuildDetailQuery(Guid id, bool includeInternal, CancellationToken cancellationToken)
    {
        var detail = await VisibleCases()
            .Where(c => c.Id == id)
            .Select(c => new
            {
                c.Id, c.TicketNumber, c.Subject, c.Description, c.Priority, c.Status, c.Queue, c.Confidential,
                c.CreatedAt, c.UpdatedAt, c.SlaDueAt, c.FirstResponseAt, c.ResolvedAt, c.ClosedAt,
                c.ResolutionCode, c.ReopenCount, c.Rating, c.RatingComment,
                CategoryName = c.Category != null ? c.Category.Name : null,
                SubcategoryName = c.Subcategory != null ? c.Subcategory.Name : null,
                AssigneeName = c.Assignee != null ? c.Assignee.DisplayName : null,
                EmployeeName = c.Employee != null ? c.Employee.DisplayName : null
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (detail is null) return null;

        var messages = await _db.Set<CaseMessage>().AsNoTracking()
            .Where(m => m.CaseId == id && (includeInternal || m.Visibility == CaseMessageVisibility.Employee))
            .OrderBy(m => m.CreatedAt)
            .Select(m => new CaseMessageDto
            {
                Id = m.Id,
                SenderName = m.SenderName,
                SenderRole = m.SenderRole,
                Visibility = m.Visibility == CaseMessageVisibility.Internal ? "Internal" : "Employee",
                Body = m.Body,
                CreatedAt = m.CreatedAt,
                AttachmentUrl = m.AttachmentUrl
            })
            .ToListAsync(cancellationToken);

        return new CaseDetailDto
        {
            Id = detail.Id,
            TicketNumber = detail.TicketNumber,
            Subject = detail.Subject,
            Description = detail.Description,
            Category = detail.CategoryName,
            Subcategory = detail.SubcategoryName,
            Priority = detail.Priority,
            Status = detail.Status,
            Queue = detail.Queue,
            Confidential = detail.Confidential,
            CreatedAt = detail.CreatedAt,
            UpdatedAt = detail.UpdatedAt,
            SlaDueAt = detail.SlaDueAt,
            FirstResponseAt = detail.FirstResponseAt,
            ResolvedAt = detail.ResolvedAt,
            ClosedAt = detail.ClosedAt,
            ResolutionCode = detail.ResolutionCode,
            ReopenCount = detail.ReopenCount,
            Rating = detail.Rating,
            RatingComment = detail.RatingComment,
            AssigneeName = detail.AssigneeName,
            EmployeeName = detail.EmployeeName,
            CanReply = detail.Status is CaseStatus.Open or CaseStatus.WaitingOnEmployee
                or CaseStatus.WaitingOnHR or CaseStatus.Reopened,
            Messages = messages
        };
    }

    private static CaseMessageDto MapMessage(CaseMessage message) => new()
    {
        Id = message.Id,
        SenderName = message.SenderName,
        SenderRole = message.SenderRole,
        Visibility = message.Visibility == CaseMessageVisibility.Internal ? "Internal" : "Employee",
        Body = message.Body,
        CreatedAt = message.CreatedAt,
        AttachmentUrl = message.AttachmentUrl
    };

    private async Task<string> NextTicketNumberAsync(DateTime now, CancellationToken cancellationToken)
    {
        var prefix = $"CS-{now.Year:D4}-";
        var sequence = await _db.Set<HelpdeskCase>().AsNoTracking()
            .CountAsync(c => c.TicketNumber.StartsWith(prefix), cancellationToken) + 1;
        return prefix + sequence.ToString("D4");
    }

    private static string QueueFor(CaseCategory category) => category.Confidential ? "Restricted" : category.Name switch
    {
        "Payroll" => "Payroll",
        "IT" => "IT",
        "Workplace" => "Facilities",
        "Leave" or "Attendance" or "Policy" or "Onboarding" or "Benefits" or "General HR" => "HR",
        _ => "General HR"
    };

    private static int SlaHours(CasePriority priority) => priority switch
    {
        CasePriority.Urgent => 4,
        CasePriority.High => 8,
        CasePriority.Medium => 24,
        _ => 48
    };

    /// <summary>Users who manage cases (case.manage or SuperAdmin's "*").</summary>
    private IQueryable<Guid> CaseAgentUserIds() =>
        from ua in _db.UserAccounts.AsNoTracking()
        where ua.Status == UserStatus.Active
        where ua.UserRoles.Any(r => r.Role.RolePermissions.Any(p =>
            p.PermissionCode == Permissions.CaseManage || p.PermissionCode == Permissions.All))
        select ua.Id;

    private async Task NotifyCaseAgentsAsync(HelpdeskCase helpdeskCase, string type, string title, string body,
        CancellationToken cancellationToken)
    {
        var agentIds = await CaseAgentUserIds()
            .Where(userId => userId != _currentUser.UserId)
            .ToListAsync(cancellationToken);

        foreach (var userId in agentIds)
        {
            // BR-013: confidential case traffic never carries detail in a notification payload.
            await _notifications.PublishAsync(new NotificationMessage(
                userId, type,
                helpdeskCase.Confidential ? "Update on a confidential HR case" : title,
                helpdeskCase.Confidential
                    ? "A confidential helpdesk case has a new update. Open the app to view details."
                    : body,
                $"/cases/{helpdeskCase.Id}", "Case", helpdeskCase.Confidential), cancellationToken);
        }
    }

    private async Task NotifyEmployeeAsync(HelpdeskCase helpdeskCase, string type, string title, string body,
        CancellationToken cancellationToken)
    {
        var userAccountId = await _db.UserAccounts.AsNoTracking()
            .Where(u => u.EmployeeId == helpdeskCase.EmployeeId && u.Status == UserStatus.Active)
            .Select(u => u.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (userAccountId == Guid.Empty) return;

        await _notifications.PublishAsync(new NotificationMessage(
            userAccountId, type,
            helpdeskCase.Confidential ? "Update on your confidential HR case" : title,
            helpdeskCase.Confidential
                ? "There is an update on your confidential helpdesk case. Open the app to view details."
                : body,
            $"/cases/{helpdeskCase.Id}", "Case", helpdeskCase.Confidential, helpdeskCase.EmployeeId),
            cancellationToken);
    }
}
