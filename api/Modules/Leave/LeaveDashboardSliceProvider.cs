using Wintech.Hrms.Api.Common.CurrentUser;
using Wintech.Hrms.Api.Common.Dashboard;

namespace Wintech.Hrms.Api.Modules.Leave;

/// <summary>
/// Contributes the <c>leaveBalances</c> key of GET /me/dashboard
/// (docs/API_CONTRACT.md §5, FR-EMP-010/014).
/// </summary>
public sealed class LeaveDashboardSliceProvider : IDashboardSliceProvider
{
    private readonly ILeaveService _leave;
    private readonly ICurrentUser _currentUser;

    public LeaveDashboardSliceProvider(ILeaveService leave, ICurrentUser currentUser)
    {
        _leave = leave;
        _currentUser = currentUser;
    }

    public int Order => 101;

    public async Task<IReadOnlyDictionary<string, object?>> GetSliceAsync(DashboardContext context,
        CancellationToken cancellationToken = default)
    {
        if (_currentUser.EmployeeId is null && context.EmployeeId is null)
            return new Dictionary<string, object?>();

        var balances = await _leave.GetBalancesAsync(context.Today.Year, cancellationToken);
        var dashboardBalances = balances
            .Select(b => new DashboardLeaveBalanceDto(b.LeaveTypeId, b.Code, b.Name,
                b.Available, b.Used, b.Pending, b.Accrued, b.CarryForward))
            .ToList();

        return new Dictionary<string, object?> { ["leaveBalances"] = dashboardBalances };
    }
}
