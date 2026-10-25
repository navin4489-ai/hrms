using Wintech.Hrms.Api.Common.Paging;
using Wintech.Hrms.Api.Common.Registration;

namespace Wintech.Hrms.Api.Modules.Leave;

/// <summary>Leave types, balances, policies, requests and calendars (docs/API_CONTRACT.md §8, FR-EMP-040..047).</summary>
public interface ILeaveService : IAppService
{
    Task<IReadOnlyList<LeaveTypeDto>> GetTypesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LeaveBalanceDto>> GetBalancesAsync(int? year, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LeavePolicyDto>> GetPoliciesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminLeaveTypeDto>> GetAdminLeaveTypesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminLeavePolicyDto>> GetAdminLeavePoliciesAsync(CancellationToken cancellationToken = default);

    Task<AdminLeaveTypeDto> UpsertLeaveTypeAsync(Guid? id, UpsertLeaveTypeRequest request, CancellationToken cancellationToken = default);

    Task<AdminLeavePolicyDto> UpsertLeavePolicyAsync(Guid? id, UpsertLeavePolicyRequest request, CancellationToken cancellationToken = default);

    /// <summary>Submit a leave request; validated against balance/window/attachment rules (FR-EMP-041/042).</summary>
    Task<LeaveRequestCreatedDto> CreateRequestAsync(LeaveRequestCreateDto request, CancellationToken cancellationToken = default);

    Task<PagedResult<LeaveRequestListItem>> ListRequestsAsync(string? status, int? year, int? month,
        bool team, Guid? employeeId, PageRequest page, CancellationToken cancellationToken = default);

    Task<LeaveRequestDetail> GetRequestAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Cancel a still-pending request; releases the held balance (FR-EMP-044).</summary>
    Task<LeaveCancelledDto> CancelRequestAsync(Guid id, string reason, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LeaveCalendarEntry>> GetCalendarAsync(DateOnly from, DateOnly to,
        CancellationToken cancellationToken = default);
}
