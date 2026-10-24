using Wintech.Hrms.Api.Common.Registration;

namespace Wintech.Hrms.Api.Common.Dashboard;

/// <summary>Everything a dashboard slice provider needs to compute its numbers for the caller.</summary>
public sealed record DashboardContext(Guid UserId, Guid? EmployeeId, DateOnly Today);

/// <summary>
/// Lets a feature module contribute a slice of <c>GET /api/v1/me/dashboard</c> without the Me module
/// having to know anything about that module's entities (modular-monolith rule: no cross-module type
/// references). Implementations are auto-registered through <see cref="IAppService"/>.
/// <para>
/// Each provider returns one or more top-level keys of the dashboard payload (e.g. "attendance",
/// "leaveBalances", "holidays", "announcements", "notificationsUnread") — see docs/API_CONTRACT.md §5.
/// Keys are merged in ascending <see cref="Order"/>; later providers never overwrite earlier ones.
/// </para>
/// </summary>
public interface IDashboardSliceProvider : IAppService
{
    /// <summary>Lower runs first. Core slices use 0-99, module slices 100+.</summary>
    int Order { get; }

    Task<IReadOnlyDictionary<string, object?>> GetSliceAsync(DashboardContext context,
        CancellationToken cancellationToken = default);
}
