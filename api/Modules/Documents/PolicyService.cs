using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.Auditing;
using Wintech.Hrms.Api.Common.CurrentUser;
using Wintech.Hrms.Api.Common.ErrorHandling;
using Wintech.Hrms.Api.Common.Registration;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Documents;

namespace Wintech.Hrms.Api.Modules.Documents;

/// <summary>
/// Company policies and versioned acknowledgements (contract §11, FR-EMP-074, FR-ADM-081/083).
/// An acknowledgement always records the policy version that was shown to the employee.
/// </summary>
public interface IPolicyService : IAppService
{
    Task<IReadOnlyList<CompanyPolicyDto>> GetPoliciesAsync(string? category, string? acknowledgementStatus, CancellationToken cancellationToken = default);
    Task<PolicyDetailDto> GetPolicyAsync(Guid id, CancellationToken cancellationToken = default);
    Task<AcknowledgePolicyResponse> AcknowledgeAsync(Guid id, string? comment, CancellationToken cancellationToken = default);
}

public sealed class PolicyService : IPolicyService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;
    private readonly IClock _clock;

    public PolicyService(AppDbContext db, ICurrentUser currentUser, IAuditService audit, IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
    }

    public async Task<IReadOnlyList<CompanyPolicyDto>> GetPoliciesAsync(
        string? category, string? acknowledgementStatus, CancellationToken cancellationToken = default)
    {
        _currentUser.EnsureActive();
        var employeeId = RequireEmployeeId();

        var query = _db.Set<CompanyPolicy>().AsNoTracking().Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(p => p.Category == category);

        AcknowledgementFilter filter;
        if (string.IsNullOrWhiteSpace(acknowledgementStatus))
        {
            filter = AcknowledgementFilter.All;
        }
        else if (string.Equals(acknowledgementStatus, "Acknowledged", StringComparison.OrdinalIgnoreCase))
        {
            filter = AcknowledgementFilter.Acknowledged;
        }
        else if (string.Equals(acknowledgementStatus, "Pending", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(acknowledgementStatus, "NotAcknowledged", StringComparison.OrdinalIgnoreCase))
        {
            filter = AcknowledgementFilter.NotAcknowledged;
        }
        else
        {
            throw new RequestValidationException(errors: new()
            {
                ["acknowledgementStatus"] = ["Allowed values: Acknowledged, Pending."]
            });
        }

        var acknowledgements = await _db.Set<PolicyAcknowledgement>().AsNoTracking()
            .Where(a => a.EmployeeId == employeeId)
            .Select(a => new PolicyAcknowledgementRow(a.PolicyId, a.PolicyVersion, a.AcknowledgedAt))
            .ToListAsync(cancellationToken);

        var policies = await query.OrderByDescending(p => p.PublishedAt).ToListAsync(cancellationToken);

        return policies
            .Select(p => ToDto(p, acknowledgements, out var acknowledgedAt))
            .Where(dto => filter switch
            {
                AcknowledgementFilter.Acknowledged => dto.AcknowledgedAt is not null,
                AcknowledgementFilter.NotAcknowledged => dto.RequiresAcknowledgement && dto.AcknowledgedAt is null,
                _ => true
            })
            .ToList();
    }

    public async Task<PolicyDetailDto> GetPolicyAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _currentUser.EnsureActive();
        var employeeId = RequireEmployeeId();

        var policy = await _db.Set<CompanyPolicy>().AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id && p.IsActive, cancellationToken)
            ?? throw new NotFoundException("The requested policy was not found.");

        var acknowledgedAt = await _db.Set<PolicyAcknowledgement>().AsNoTracking()
            .Where(a => a.PolicyId == policy.Id && a.EmployeeId == employeeId && a.PolicyVersion == policy.Version)
            .Select(a => (DateTime?)a.AcknowledgedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return new PolicyDetailDto(
            policy.Id, policy.Title, policy.Category, policy.Summary, policy.Body, policy.Version,
            policy.PublishedAt, policy.EffectiveFrom, policy.RequiresAcknowledgement,
            acknowledgedAt, policy.DocumentId);
    }

    public async Task<AcknowledgePolicyResponse> AcknowledgeAsync(Guid id, string? comment, CancellationToken cancellationToken = default)
    {
        _currentUser.EnsureActive();
        var employeeId = RequireEmployeeId();

        var policy = await _db.Set<CompanyPolicy>().AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id && p.IsActive, cancellationToken)
            ?? throw new NotFoundException("The requested policy was not found.");

        var already = await _db.Set<PolicyAcknowledgement>().AsNoTracking()
            .AnyAsync(a => a.PolicyId == policy.Id && a.EmployeeId == employeeId && a.PolicyVersion == policy.Version,
                cancellationToken);

        // FR-EMP-074: acknowledgements are versioned — acknowledging the same version again is a 409.
        if (already)
            throw new ConflictException($"You have already acknowledged version {policy.Version} of this policy.");

        var now = _clock.UtcNow;
        var acknowledgement = new PolicyAcknowledgement
        {
            Id = Guid.NewGuid(),
            PolicyId = policy.Id,
            EmployeeId = employeeId,
            UserId = _currentUser.RequireUserId(),
            PolicyVersion = policy.Version,
            AcknowledgedAt = now,
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim()
        };
        _db.Set<PolicyAcknowledgement>().Add(acknowledgement);

        _audit.Add("Policy.Acknowledge", "CompanyPolicy", policy.Id.ToString(),
            details: new { policy.Title, policy.Version, acknowledgement.Comment });
        await _db.SaveChangesAsync(cancellationToken);

        return new AcknowledgePolicyResponse(now, policy.Version);
    }

    private static CompanyPolicyDto ToDto(CompanyPolicy policy,
        IReadOnlyList<PolicyAcknowledgementRow> acknowledgements, out DateTime? acknowledgedAt)
    {
        var match = acknowledgements.FirstOrDefault(a =>
            a.PolicyId == policy.Id && a.PolicyVersion == policy.Version);
        acknowledgedAt = match?.AcknowledgedAt;
        return new CompanyPolicyDto(
            policy.Id, policy.Title, policy.Category, policy.Version, policy.PublishedAt,
            policy.EffectiveFrom, policy.RequiresAcknowledgement, acknowledgedAt, policy.DocumentId);
    }

    private sealed record PolicyAcknowledgementRow(Guid PolicyId, string PolicyVersion, DateTime AcknowledgedAt);

    private enum AcknowledgementFilter
    {
        All = 0,
        Acknowledged = 1,
        NotAcknowledged = 2
    }

    private Guid RequireEmployeeId() =>
        _currentUser.EmployeeId
        ?? throw new ForbiddenException("No employee record is linked to your account.");
}
