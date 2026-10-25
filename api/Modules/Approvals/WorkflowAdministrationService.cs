using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.Auditing;
using Wintech.Hrms.Api.Common.CurrentUser;
using Wintech.Hrms.Api.Common.ErrorHandling;
using Wintech.Hrms.Api.Common.Registration;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Approvals;
using Wintech.Hrms.Api.Domain.Identity;
using Wintech.Hrms.Api.Security;

namespace Wintech.Hrms.Api.Modules.Approvals;

public interface IWorkflowAdministrationService : IAppService
{
    Task<WorkflowDto> CreateVersionAsync(Guid sourceId, UpsertWorkflowVersionRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Creates immutable, audited workflow versions; in-flight tasks retain their original version snapshot.</summary>
public sealed class WorkflowAdministrationService : IWorkflowAdministrationService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;
    private readonly IClock _clock;

    public WorkflowAdministrationService(AppDbContext db, ICurrentUser currentUser,
        IAuditService audit, IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
    }

    public async Task<WorkflowDto> CreateVersionAsync(Guid sourceId, UpsertWorkflowVersionRequest request,
        CancellationToken cancellationToken = default)
    {
        _currentUser.EnsureActive();
        if (!_currentUser.HasPermission(Permissions.AdminConfig))
            throw new ForbiddenException("You do not have permission to administer approval workflows.");

        var source = await _db.ApprovalWorkflowDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == sourceId, cancellationToken)
            ?? throw new NotFoundException("Workflow definition not found.");
        if (!ApprovalRequestTypes.All.Contains(source.RequestType, StringComparer.OrdinalIgnoreCase))
            throw new RequestValidationException("The workflow request type is not supported.");
        if (request.Steps.Count is < 1 or > 10)
            throw new RequestValidationException("A workflow must have between one and ten steps.",
                new() { ["steps"] = ["Add at least one and no more than ten approval steps."] });

        var normalizedSteps = new List<(UpsertWorkflowStepRequest Request, ApproverType Type, string? Value)>();
        foreach (var step in request.Steps)
        {
            if (step.ApproverType is not { } approverType || !Enum.IsDefined(approverType))
                throw new RequestValidationException("An approver type is invalid.",
                    new() { ["steps"] = ["Each step must use Manager, Role, NamedUser or SelfAndManager."] });
            var value = string.IsNullOrWhiteSpace(step.ApproverValue) ? null : step.ApproverValue.Trim();
            if (approverType is ApproverType.Role or ApproverType.SelfAndManager && value is null)
                throw new RequestValidationException("The approver value is required.",
                    new() { ["steps"] = ["Role and fallback approvers require an approver value."] });
            if (approverType == ApproverType.Role &&
                !await _db.Roles.AnyAsync(role => role.Name == value, cancellationToken))
                throw new RequestValidationException("The selected approver role does not exist.",
                    new() { ["steps"] = [$"Role '{value}' does not exist."] });
            if (approverType == ApproverType.NamedUser &&
                (!Guid.TryParse(value, out var userId) ||
                 !await _db.UserAccounts.AnyAsync(user => user.Id == userId && user.Status == UserStatus.Active,
                     cancellationToken)))
                throw new RequestValidationException("The named approver is not an active user.",
                    new() { ["steps"] = ["Choose an active user account for a named approver."] });
            normalizedSteps.Add((step, approverType, value));
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var version = (await _db.ApprovalWorkflowDefinitions
            .Where(item => item.Code == source.Code)
            .MaxAsync(item => (int?)item.Version, cancellationToken) ?? 0) + 1;
        var superseded = await _db.ApprovalWorkflowDefinitions
            .Where(item => item.RequestType == source.RequestType && item.IsActive)
            .ToListAsync(cancellationToken);
        foreach (var existing in superseded) existing.IsActive = false;

        var workflow = new ApprovalWorkflowDefinition
        {
            Id = Guid.NewGuid(),
            Code = source.Code,
            Name = request.Name.Trim(),
            RequestType = source.RequestType,
            Version = version,
            IsActive = true,
            CreatedAt = _clock.UtcNow,
            Steps = normalizedSteps.Select((step, index) => new ApprovalWorkflowStep
            {
                Id = Guid.NewGuid(),
                StepNumber = index + 1,
                Name = step.Request.Name.Trim(),
                ApproverType = step.Type,
                ApproverValue = step.Value,
                SLADays = step.Request.SlaDays
            }).ToList()
        };
        _db.ApprovalWorkflowDefinitions.Add(workflow);
        _audit.Add("workflow.version.create", nameof(ApprovalWorkflowDefinition), workflow.Id.ToString(),
            details: new
            {
                workflow.Code,
                workflow.RequestType,
                workflow.Version,
                supersededVersions = superseded.Select(item => item.Version).ToArray(),
                stepCount = workflow.Steps.Count
            });
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new WorkflowDto(
            workflow.Id,
            workflow.Code,
            workflow.Name,
            workflow.Version,
            workflow.IsActive,
            workflow.Steps.OrderBy(step => step.StepNumber)
                .Select(step => new WorkflowStepDto(step.StepNumber, step.Name, step.ApproverType,
                    step.ApproverValue, step.SLADays)).ToArray());
    }
}
