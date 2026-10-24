using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wintech.Hrms.Api.Common.Auditing;
using Wintech.Hrms.Api.Common.Configuration;
using Wintech.Hrms.Api.Common.CurrentUser;
using Wintech.Hrms.Api.Common.ErrorHandling;
using Wintech.Hrms.Api.Common.Files;
using Wintech.Hrms.Api.Common.Notifications;
using Wintech.Hrms.Api.Common.Paging;
using Wintech.Hrms.Api.Common.Registration;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Approvals;
using Wintech.Hrms.Api.Domain.Expenses;

namespace Wintech.Hrms.Api.Modules.Expenses;

/// <summary>
/// Expense claims and travel requests (FR-EMP-080..084, FR-ADM-100..103).
/// Every submission is backed by an approval workflow (RequestType "Expense"/"Travel");
/// the <see cref="ExpenseApprovalHandler"/>/<see cref="TravelApprovalHandler"/> apply the
/// final decision back onto the domain record exactly once.
/// </summary>
public interface IExpenseService : IAppService
{
    Task<ExpenseCreatedResult> CreateClaimAsync(CreateExpenseInput input, CancellationToken cancellationToken = default);
    Task<PagedResult<ExpenseListItem>> ListClaimsAsync(ExpenseQuery query, PageRequest page, CancellationToken cancellationToken = default);
    Task<ExpenseListItem> GetClaimAsync(Guid id, CancellationToken cancellationToken = default);
    Task<StoredFileStream> OpenReceiptAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ExpenseListItem> CancelClaimAsync(Guid id, string reason, CancellationToken cancellationToken = default);
    Task<TravelCreatedResult> CreateTravelRequestAsync(CreateTravelRequestInput input, CancellationToken cancellationToken = default);
    Task<PagedResult<TravelListItem>> ListTravelRequestsAsync(PageRequest page, CancellationToken cancellationToken = default);
}

public sealed class ExpenseService : IExpenseService
{
    /// <summary>BR-010 receipt allow-list (images and PDFs only) — enforced by PrivateFileStore.</summary>
    private static readonly string[] ReceiptExtensions = [".pdf", ".png", ".jpg", ".jpeg"];
    private static readonly string[] SupportedCurrencies = ["INR", "USD", "EUR", "GBP", "AED"];
    private static readonly string[] ExpenseCategories =
        ["Meals", "Travel", "Lodging", "LocalTransport", "OfficeSupplies", "Communication", "Other"];

    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;
    private readonly INotificationPublisher _notifications;
    private readonly IClock _clock;
    private readonly IPrivateFileStore _files;
    private readonly SecurityOptions _security;
    private readonly IApprovalService? _approval;

    public ExpenseService(AppDbContext db, ICurrentUser currentUser, IAuditService audit,
        INotificationPublisher notifications, IClock clock, IPrivateFileStore files,
        IOptions<SecurityOptions> securityOptions, IServiceProvider serviceProvider)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _notifications = notifications;
        _clock = clock;
        _files = files;
        _security = securityOptions.Value;
        // The Approvals module supplies the workflow engine. Resolved lazily so this module also
        // runs standalone (seed/dev sandboxes); the engine is used whenever it is registered.
        _approval = serviceProvider.GetService<IApprovalService>();
    }

    // ---------------------------------------------------------------- claims

    public async Task<ExpenseCreatedResult> CreateClaimAsync(CreateExpenseInput input,
        CancellationToken cancellationToken = default)
    {
        _currentUser.EnsureActive();
        var employeeId = RequireEmployeeId();

        if (!ExpenseCategories.Contains(input.Category, StringComparer.OrdinalIgnoreCase))
            throw new RequestValidationException(errors: new()
            {
                ["category"] = [$"Allowed categories: {string.Join(", ", ExpenseCategories)}."]
            });

        if (input.Amount <= 0)
            throw new RequestValidationException(errors: new() { ["amount"] = ["Amount must be greater than zero."] });

        // FR-ADM-100: per-claim limit from configuration (BR-012: applies to new claims only).
        if (input.Amount > _security.ExpensePerClaimLimit)
            throw new RequestValidationException("Amount exceeds the per-claim limit.",
                new() { ["amount"] = [$"Maximum per claim is {_security.ExpensePerClaimLimit:N0}."] });

        if (input.ExpenseDate > _clock.Today)
            throw new RequestValidationException(errors: new()
            {
                ["expenseDate"] = ["Expense date cannot be in the future."]
            });

        var currency = string.IsNullOrWhiteSpace(input.Currency) ? "INR" : input.Currency.Trim().ToUpperInvariant();
        if (!SupportedCurrencies.Contains(currency, StringComparer.Ordinal))
            throw new RequestValidationException(errors: new()
            {
                ["currency"] = [$"Supported currencies: {string.Join(", ", SupportedCurrencies)}."]
            });

        if (input.ReceiptBytes is not { Length: > 0 })
            throw new RequestValidationException(errors: new() { ["receipt"] = ["A receipt file is required."] });

        // FR-EMP-081: duplicate receipt detection on the file hash (409).
        var receiptHash = Convert.ToHexString(SHA256.HashData(input.ReceiptBytes)).ToLowerInvariant();
        var duplicate = await _db.Set<ExpenseClaim>().AsNoTracking()
            .AnyAsync(c => c.ReceiptSha256 == receiptHash, cancellationToken);
        if (duplicate)
            throw new ConflictException("This receipt has already been submitted with another claim.");

        // BR-010: allow-list + max size enforced by the private file store (400 on violation).
        await using var receiptStream = new MemoryStream(input.ReceiptBytes, writable: false);
        var stored = await _files.SaveAsync(receiptStream, input.ReceiptFileName,
            _security.MaxUploadBytes, ReceiptExtensions, cancellationToken);

        var claim = new ExpenseClaim
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            Category = input.Category.Trim(),
            Amount = input.Amount,
            Currency = currency,
            ExpenseDate = input.ExpenseDate,
            BusinessPurpose = input.BusinessPurpose.Trim(),
            CostCentre = string.IsNullOrWhiteSpace(input.CostCentre) ? null : input.CostCentre.Trim(),
            Status = ExpenseClaimStatus.Pending,
            SubmittedAt = _clock.UtcNow,
            ReceiptStorageKey = stored.StorageKey,
            ReceiptFileName = stored.OriginalFileName,
            ReceiptContentType = stored.ContentType,
            ReceiptSizeBytes = stored.SizeBytes,
            ReceiptSha256 = receiptHash
        };
        _db.Set<ExpenseClaim>().Add(claim);

        var approvalTask = await StartApprovalAsync("Expense", claim.Id,
            $"Expense claim — {claim.Category} {claim.Amount:N0} {claim.Currency}",
            claim.Amount, claim.Currency, cancellationToken);
        claim.ApprovalTaskId = approvalTask;

        _audit.Add("Expense.Submitted", nameof(ExpenseClaim), claim.Id.ToString(),
            details: new { claim.Category, claim.Amount, claim.Currency, receiptBytes = stored.SizeBytes });
        await _db.SaveChangesAsync(cancellationToken);

        return new ExpenseCreatedResult { Id = claim.Id, Status = claim.Status, ApprovalTaskId = approvalTask };
    }

    public async Task<PagedResult<ExpenseListItem>> ListClaimsAsync(ExpenseQuery query, PageRequest page,
        CancellationToken cancellationToken = default)
    {
        _currentUser.EnsureActive();
        var privileged = CanSeeAllExpenses();
        var ownEmployeeId = _currentUser.EmployeeId;

        IQueryable<ExpenseClaim> claims = _db.Set<ExpenseClaim>().AsNoTracking();

        if (query.Team)
        {
            // Team view: direct reports of the caller; callers without the permission see only their own.
            var reportIds = await ReportEmployeeIdsAsync(cancellationToken);
            claims = claims.Where(c => reportIds.Contains(c.EmployeeId));
        }
        else if (!privileged)
        {
            claims = claims.Where(c => ownEmployeeId != null && c.EmployeeId == ownEmployeeId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (!Enum.TryParse<ExpenseClaimStatus>(query.Status.Trim(), ignoreCase: true, out var status))
                throw new RequestValidationException(errors: new() { ["status"] = ["Unknown expense status."] });
            claims = claims.Where(c => c.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Month))
        {
            if (!TryParseMonth(query.Month, out var from, out var to))
                throw new RequestValidationException(errors: new() { ["month"] = ["Expected format YYYY-MM."] });
            claims = claims.Where(c => c.SubmittedAt >= from && c.SubmittedAt < to);
        }

        var ordered = claims.OrderByDescending(c => c.SubmittedAt);
        // Receipts are visible to their owner and to privileged callers (contract section 12).
        var result = await ordered.ToPagedAsync(page,
            c => MapListItem(c, privileged || (ownEmployeeId != null && c.EmployeeId == ownEmployeeId.Value)),
            cancellationToken);

        // Display status: surface the intermediate manager step while the finance step is pending.
        var stepByTask = await CurrentApprovalStepsAsync(result.Items.Select(i => i.ApprovalTaskId), cancellationToken);
        return PagedResult<ExpenseListItem>.Create(
            result.Items.Select(i => stepByTask.TryGetValue(i.ApprovalTaskId ?? Guid.Empty, out var step) && step >= 2
                ? i with { Status = ExpenseClaimStatus.ManagerApproved }
                : i).ToList(),
            result.Page, result.PageSize, result.Total);
    }

    public async Task<ExpenseListItem> GetClaimAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _currentUser.EnsureActive();
        var privileged = CanSeeAllExpenses();
        var claim = await _db.Set<ExpenseClaim>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                    ?? throw new NotFoundException("The expense claim was not found.");
        EnsureVisible(claim.EmployeeId, privileged);

        var item = MapListItem(claim, includeReceipt: privileged || claim.EmployeeId == _currentUser.EmployeeId);
        var stepByTask = await CurrentApprovalStepsAsync([item.ApprovalTaskId], cancellationToken);
        return stepByTask.TryGetValue(item.ApprovalTaskId ?? Guid.Empty, out var step) && step >= 2
            ? item with { Status = ExpenseClaimStatus.ManagerApproved }
            : item;
    }

    public async Task<StoredFileStream> OpenReceiptAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _currentUser.EnsureActive();
        var privileged = CanSeeAllExpenses();
        var claim = await _db.Set<ExpenseClaim>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                    ?? throw new NotFoundException("The expense claim was not found.");
        EnsureVisible(claim.EmployeeId, privileged);

        if (string.IsNullOrWhiteSpace(claim.ReceiptStorageKey))
            throw new NotFoundException("This claim has no receipt attached.");

        _audit.Add("Expense.ReceiptDownloaded", nameof(ExpenseClaim), claim.Id.ToString(),
            details: new { claim.EmployeeId });
        await _db.SaveChangesAsync(cancellationToken);

        return await _files.OpenReadAsync(claim.ReceiptStorageKey, cancellationToken);
    }

    public async Task<ExpenseListItem> CancelClaimAsync(Guid id, string reason, CancellationToken cancellationToken = default)
    {
        _currentUser.EnsureActive();
        var claim = await _db.Set<ExpenseClaim>().FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                    ?? throw new NotFoundException("The expense claim was not found.");
        // Cancelling is the requester's own action — other people's claims are 404.
        EnsureVisible(claim.EmployeeId, privileged: false);

        if (claim.Status is not (ExpenseClaimStatus.Pending or ExpenseClaimStatus.Draft))
            throw new ConflictException("Only claims still awaiting approval can be cancelled.");

        if (_approval is not null)
            await _approval.CancelAsync("Expense", claim.Id, reason, cancellationToken);

        claim.Status = ExpenseClaimStatus.Cancelled;
        if (claim.ApprovalTaskId is { } taskId)
        {
            var task = await _db.ApprovalTasks.FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
            if (task is { Status: nameof(ApprovalStatus.Pending) })
            {
                task.Status = nameof(ApprovalStatus.Cancelled);
                task.CompletedAt = _clock.UtcNow;
                task.UpdatedAt = _clock.UtcNow;
            }
        }

        _audit.Add("Expense.Cancelled", nameof(ExpenseClaim), claim.Id.ToString(), details: new { reason });
        await _db.SaveChangesAsync(cancellationToken);

        await NotifyAsync(claim.EmployeeId, "Expense", "Expense claim cancelled",
            $"Your claim ({claim.Category} {claim.Amount:N0} {claim.Currency}) was cancelled.");

        return MapListItem(claim, includeReceipt: true);
    }

    // ---------------------------------------------------------------- travel requests

    public async Task<TravelCreatedResult> CreateTravelRequestAsync(CreateTravelRequestInput input,
        CancellationToken cancellationToken = default)
    {
        _currentUser.EnsureActive();
        var employeeId = RequireEmployeeId();

        if (input.EndDate < input.StartDate)
            throw new BusinessRuleException(errors: new()
            {
                ["endDate"] = ["End date must be on or after start date."]
            });

        if (input.StartDate < _clock.Today)
            throw new BusinessRuleException(errors: new()
            {
                ["startDate"] = ["Travel start date cannot be in the past."]
            });

        var currency = string.IsNullOrWhiteSpace(input.Currency) ? "INR" : input.Currency.Trim().ToUpperInvariant();
        if (!SupportedCurrencies.Contains(currency, StringComparer.Ordinal))
            throw new RequestValidationException(errors: new()
            {
                ["currency"] = [$"Supported currencies: {string.Join(", ", SupportedCurrencies)}."]
            });

        var request = new TravelRequest
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            Destination = input.Destination.Trim(),
            Purpose = input.Purpose.Trim(),
            StartDate = input.StartDate,
            EndDate = input.EndDate,
            EstimatedCost = input.EstimatedCost,
            Currency = currency,
            Status = TravelRequestStatus.Pending,
            SubmittedAt = _clock.UtcNow
        };
        _db.Set<TravelRequest>().Add(request);

        var approvalTask = await StartApprovalAsync("Travel", request.Id,
            $"Travel request — {request.Destination} ({request.StartDate:yyyy-MM-dd})",
            request.EstimatedCost, request.Currency, cancellationToken);
        request.ApprovalTaskId = approvalTask;

        _audit.Add("Travel.Submitted", nameof(TravelRequest), request.Id.ToString(),
            details: new { request.Destination, request.StartDate, request.EndDate, request.EstimatedCost });
        await _db.SaveChangesAsync(cancellationToken);

        return new TravelCreatedResult { Id = request.Id, Status = request.Status, ApprovalTaskId = approvalTask };
    }

    public async Task<PagedResult<TravelListItem>> ListTravelRequestsAsync(PageRequest page,
        CancellationToken cancellationToken = default)
    {
        _currentUser.EnsureActive();
        var privileged = CanSeeAllExpenses();
        var ownEmployeeId = _currentUser.EmployeeId;

        IQueryable<TravelRequest> requests = _db.Set<TravelRequest>().AsNoTracking();
        if (!privileged)
            requests = requests.Where(t => ownEmployeeId != null && t.EmployeeId == ownEmployeeId.Value);

        var result = await requests.OrderByDescending(t => t.SubmittedAt)
            .ToPagedAsync(page, MapTravelItem, cancellationToken);

        // Employee names only for privileged viewers (own records need no name).
        if (!privileged || result.Items.Count == 0) return result;

        var names = await EmployeeNamesAsync(result.Items.Select(i => i.Id).ToList(), result, cancellationToken);
        return PagedResult<TravelListItem>.Create(
            result.Items.Select(i => names.TryGetValue(i.Id, out var name) ? i with { EmployeeName = name } : i).ToList(),
            result.Page, result.PageSize, result.Total);
    }

    // ---------------------------------------------------------------- approval integration

    /// <summary>
    /// Starts the workflow through the Approvals module when it is registered; otherwise a
    /// minimal pending task is written directly so the module works standalone (dev sandbox).
    /// </summary>
    private async Task<Guid> StartApprovalAsync(string requestType, Guid requestId, string title,
        decimal amount, string currency, CancellationToken cancellationToken)
    {
        if (_approval is not null)
        {
            var task = await _approval.StartAsync(new ApprovalStartRequest(
                requestType, requestId, title, _currentUser.UserId, _currentUser.EmployeeId,
                _currentUser.DisplayName, amount, currency), cancellationToken);
            return task.Id;
        }

        var direct = new ApprovalTask
        {
            Id = Guid.NewGuid(),
            RequestType = requestType,
            RequestId = requestId,
            Title = title,
            RequesterUserId = _currentUser.UserId,
            RequesterEmployeeId = _currentUser.EmployeeId,
            RequesterName = _currentUser.DisplayName,
            Amount = amount,
            Currency = currency,
            Status = nameof(ApprovalStatus.Pending),
            CurrentStepNumber = 1,
            CurrentStepName = "Line manager",
            CreatedAt = _clock.UtcNow,
            UpdatedAt = _clock.UtcNow,
            Version = 1
        };
        _db.ApprovalTasks.Add(direct);
        return direct.Id;
    }

    /// <summary>Task id -> current step for still-pending tasks (used to display ManagerApproved).</summary>
    private async Task<IReadOnlyDictionary<Guid, int>> CurrentApprovalStepsAsync(
        IEnumerable<Guid?> taskIds, CancellationToken cancellationToken)
    {
        var ids = taskIds.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<Guid, int>();

        var rows = await _db.ApprovalTasks.AsNoTracking()
            .Where(t => ids.Contains(t.Id) && t.Status == nameof(ApprovalStatus.Pending))
            .Select(t => new { t.Id, t.CurrentStepNumber })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.Id, r => r.CurrentStepNumber);
    }

    // ---------------------------------------------------------------- helpers

    private Guid RequireEmployeeId() => _currentUser.EmployeeId
        ?? throw new BusinessRuleException("No employee record is linked to your account. Contact HR.");

    /// <summary>expense.approve / expense.finance open up team-wide visibility (contract section 12).</summary>
    private bool CanSeeAllExpenses() =>
        _currentUser.HasPermission(Security.Permissions.ExpenseApprove) ||
        _currentUser.HasPermission(Security.Permissions.ExpenseFinance);

    private async Task<HashSet<Guid>> ReportEmployeeIdsAsync(CancellationToken cancellationToken)
    {
        var own = _currentUser.EmployeeId;
        if (own is null) return [];
        return await _db.Employees.AsNoTracking()
            .Where(e => e.ManagerId == own.Value)
            .Select(e => e.Id)
            .ToHashSetAsync(cancellationToken);
    }

    private void EnsureVisible(Guid recordEmployeeId, bool privileged)
    {
        if (privileged) return;
        if (_currentUser.EmployeeId is null || recordEmployeeId != _currentUser.EmployeeId.Value)
            throw new NotFoundException("The expense claim was not found.");
    }

    private async Task<Dictionary<Guid, string>> EmployeeNamesAsync(
        List<Guid> requestIds, PagedResult<TravelListItem> page, CancellationToken cancellationToken)
    {
        var employeeIds = await _db.Set<TravelRequest>().AsNoTracking()
            .Where(t => requestIds.Contains(t.Id))
            .Select(t => new { t.Id, t.EmployeeId })
            .ToListAsync(cancellationToken);
        var ids = employeeIds.Select(r => r.EmployeeId).Distinct().ToArray();
        var names = await _db.Employees.AsNoTracking()
            .Where(e => ids.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.DisplayName, cancellationToken);
        return employeeIds
            .Where(r => names.ContainsKey(r.EmployeeId))
            .ToDictionary(r => r.Id, r => names[r.EmployeeId]);
    }

    private async Task NotifyAsync(Guid? employeeId, string type, string title, string body)
    {
        if (employeeId is null) return;
        var userId = await _db.UserAccounts.AsNoTracking()
            .Where(u => u.EmployeeId == employeeId.Value && u.Status == Domain.Identity.UserStatus.Active)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync();
        if (userId is null) return;

        await _notifications.PublishAsync(new NotificationMessage(
            userId, type, title, body, DeepLink: "/expenses", Category: "Expense",
            EmployeeId: employeeId));
    }

    private static bool TryParseMonth(string value, out DateTime from, out DateTime to)
    {
        from = default;
        to = default;
        if (!DateOnly.TryParseExact(value.Trim(), "yyyy-MM", out var month)) return false;
        from = DateTime.SpecifyKind(month.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        to = DateTime.SpecifyKind(month.AddMonths(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        return true;
    }

    private static ExpenseListItem MapListItem(ExpenseClaim claim, bool includeReceipt) => new()
    {
        Id = claim.Id,
        Category = claim.Category,
        Amount = claim.Amount,
        Currency = claim.Currency,
        ExpenseDate = claim.ExpenseDate,
        BusinessPurpose = claim.BusinessPurpose,
        CostCentre = claim.CostCentre,
        Status = claim.Status,
        SubmittedAt = claim.SubmittedAt,
        ApprovedAt = claim.ApprovedAt,
        PaidReference = claim.PaidReference,
        RejectionReason = claim.RejectionReason,
        ApprovalTaskId = claim.ApprovalTaskId,
        ReceiptUrl = includeReceipt && !string.IsNullOrWhiteSpace(claim.ReceiptStorageKey)
            ? $"/api/v1/expenses/{claim.Id}/receipt"
            : null
    };

    private static TravelListItem MapTravelItem(TravelRequest request) => new()
    {
        Id = request.Id,
        Destination = request.Destination,
        Purpose = request.Purpose,
        StartDate = request.StartDate,
        EndDate = request.EndDate,
        EstimatedCost = request.EstimatedCost,
        Currency = request.Currency,
        Status = request.Status,
        SubmittedAt = request.SubmittedAt,
        DecidedAt = request.DecidedAt,
        RejectionReason = request.RejectionReason,
        ApprovalTaskId = request.ApprovalTaskId
    };
}
