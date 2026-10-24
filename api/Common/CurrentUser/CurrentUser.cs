using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.ErrorHandling;
using Wintech.Hrms.Api.Common.Registration;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Identity;

namespace Wintech.Hrms.Api.Common.CurrentUser;

/// <summary>
/// Server-side identity for the caller. Roles and permissions are loaded from the database
/// on every request so role changes take effect immediately (UAT-007) and suspended or
/// terminated accounts are denied at their effective time (FR-EMP-006, BR-008).
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid UserId { get; }
    Guid? EmployeeId { get; }
    string EmployeeNumber { get; }
    string DisplayName { get; }
    string? WorkEmail { get; }
    IReadOnlyCollection<string> Roles { get; }
    IReadOnlyCollection<string> Permissions { get; }
    string? RemoteIpAddress { get; }
    string? CorrelationId { get; }

    bool HasPermission(string permission);
    bool IsInRole(string role);
    /// <summary>Returns the user id or throws 401 for anonymous callers.</summary>
    Guid RequireUserId();
    /// <summary>Throws 403 when the account is suspended, terminated or deleted.</summary>
    void EnsureActive();
}

public sealed class CurrentUser : ICurrentUser
{
    private sealed record Snapshot(
        Guid UserId,
        Guid? EmployeeId,
        string EmployeeNumber,
        string DisplayName,
        string? WorkEmail,
        UserStatus Status,
        DateOnly? AccessEndsOn,
        string[] Roles,
        string[] Permissions);

    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private Snapshot? _snapshot;
    private bool _loaded;

    public CurrentUser(AppDbContext db, IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
    }

    private HttpContext? HttpContext => _httpContextAccessor.HttpContext;

    public bool IsAuthenticated => HttpContext?.User?.Identity?.IsAuthenticated == true;

    private Guid? ClaimedUserId
    {
        get
        {
            var claim = HttpContext?.User?.FindFirst("sub")?.Value
                        ?? HttpContext?.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(claim, out var id) ? id : null;
        }
    }

    public Guid UserId => ClaimedUserId ?? Guid.Empty;
    public Guid? EmployeeId => Load()?.EmployeeId;
    public string EmployeeNumber => Load()?.EmployeeNumber ?? string.Empty;
    public string DisplayName => Load()?.DisplayName ?? string.Empty;
    public string? WorkEmail => Load()?.WorkEmail;
    public IReadOnlyCollection<string> Roles => Load()?.Roles ?? [];
    public IReadOnlyCollection<string> Permissions => Load()?.Permissions ?? [];
    public string? RemoteIpAddress => HttpContext?.Connection.RemoteIpAddress?.ToString();
    public string? CorrelationId => HttpContext?.Items[ErrorHandling.ExceptionHandlingMiddleware.CorrelationItemKey] as string
                                    ?? HttpContext?.TraceIdentifier;

    public bool HasPermission(string permission)
    {
        if (!IsAuthenticated) return false;
        var snapshot = Load();
        if (snapshot is null || snapshot.Status != UserStatus.Active) return false;
        return snapshot.Permissions.Contains("*", StringComparer.Ordinal) ||
               snapshot.Permissions.Contains(permission, StringComparer.Ordinal);
    }

    public bool IsInRole(string role)
    {
        if (!IsAuthenticated) return false;
        var snapshot = Load();
        return snapshot is { Status: UserStatus.Active } && snapshot.Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
    }

    public Guid RequireUserId()
    {
        var id = ClaimedUserId;
        if (id is null) throw new UnauthorizedException();
        return id.Value;
    }

    public void EnsureActive()
    {
        if (!IsAuthenticated) throw new UnauthorizedException();
        var snapshot = Load();
        if (snapshot is null) throw new ForbiddenException("Your account could not be found.");
        if (snapshot.Status != UserStatus.Active)
            throw new ForbiddenException("Your account is not active. Contact HR if you believe this is an error.");
        if (snapshot.AccessEndsOn is { } ends && DateOnly.FromDateTime(DateTime.UtcNow) > ends)
            throw new ForbiddenException("Your access has ended. Contact HR if you believe this is an error.");
    }

    private Snapshot? Load()
    {
        if (_loaded) return _snapshot;
        var userId = ClaimedUserId;
        if (userId is null) return null;

        var query =
            from u in _db.UserAccounts.AsNoTracking()
            where u.Id == userId.Value
            select new
            {
                u.Id,
                u.EmployeeId,
                u.Status,
                u.AccessEndsOn,
                EmployeeNumber = u.Employee != null ? u.Employee.EmployeeNumber : "",
                DisplayName = u.Employee != null ? u.Employee.DisplayName : u.UserName,
                WorkEmail = u.Employee != null ? u.Employee.WorkEmail : u.Email,
                Roles = u.UserRoles.Select(r => r.Role.Name).ToArray(),
                Permissions = u.UserRoles.SelectMany(r => r.Role.RolePermissions.Select(p => p.PermissionCode)).ToArray()
            };

        var row = query.AsEnumerable().FirstOrDefault();
        _loaded = true;
        _snapshot = row is null
            ? null
            : new Snapshot(row.Id, row.EmployeeId, row.EmployeeNumber, row.DisplayName, row.WorkEmail,
                row.Status, row.AccessEndsOn, row.Roles, row.Permissions);
        return _snapshot;
    }
}
