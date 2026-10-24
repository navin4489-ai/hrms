using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.Auditing;
using Wintech.Hrms.Api.Common.CurrentUser;
using Wintech.Hrms.Api.Common.ErrorHandling;
using Wintech.Hrms.Api.Common.Paging;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Admin;
using Wintech.Hrms.Api.Domain.Audit;
using Wintech.Hrms.Api.Domain.Employees;
using Wintech.Hrms.Api.Domain.Identity;
using Wintech.Hrms.Api.Security;

namespace Wintech.Hrms.Api.Modules.Admin;

public sealed class AdminService : IAdminService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;
    private readonly IClock _clock;
    private readonly IEnumerable<IHostedService> _hostedServices;

    public AdminService(AppDbContext db, ICurrentUser currentUser, IAuditService audit, IClock clock,
        IEnumerable<IHostedService> hostedServices)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
        _hostedServices = hostedServices;
    }

    // ---------------------------------------------------------------- users

    public async Task<PagedResult<AdminUserListItem>> GetUsersAsync(string? role, string? status, string? q,
        PageRequest page, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        IQueryable<UserAccount> query = _db.UserAccounts.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(role))
        {
            var roleTerm = role.Trim().ToLower();
            query = query.Where(u => u.UserRoles.Any(ur => ur.Role!.Name.ToLower() == roleTerm));
        }

        UserStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<UserStatus>(status.Trim(), ignoreCase: true, out var parsed))
                throw new RequestValidationException("The status filter is not valid.",
                    new Dictionary<string, string[]>
                    {
                        ["status"] = ["Status must be one of: Active, Suspended, Terminated, Pending."]
                    });
            statusFilter = parsed;
            query = query.Where(u => u.Status == statusFilter);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(u =>
                u.UserName.Contains(term) ||
                (u.Employee != null && (u.Employee.EmployeeNumber.Contains(term) ||
                                        u.Employee.DisplayName.Contains(term) ||
                                        u.Employee.WorkEmail.Contains(term))));
        }

        var projected = query.OrderBy(u => u.UserName).ThenBy(u => u.Id).Select(u => new
        {
            u.Id,
            u.EmployeeId,
            EmployeeNumber = u.Employee != null ? u.Employee.EmployeeNumber : string.Empty,
            DisplayName = u.Employee != null ? u.Employee.DisplayName : u.UserName,
            WorkEmail = u.Employee != null ? u.Employee.WorkEmail : u.Email,
            u.Status,
            u.MfaEnabled,
            u.LastLoginAt,
            Roles = u.UserRoles.Select(r => r.Role!.Name).ToArray(),
            SessionsActive = u.RefreshTokens.Count(t => t.RevokedAt == null && t.ExpiresAt > now)
        });

        return await projected.ToPagedAsync(page, u => new AdminUserListItem
        {
            Id = u.Id,
            EmployeeId = u.EmployeeId,
            EmployeeNumber = u.EmployeeNumber,
            DisplayName = u.DisplayName,
            WorkEmail = u.WorkEmail,
            Roles = u.Roles.OrderBy(r => r, StringComparer.Ordinal).ToArray(),
            Status = u.Status,
            MfaEnabled = u.MfaEnabled,
            LastLoginAt = u.LastLoginAt,
            SessionsActive = u.SessionsActive
        }, cancellationToken);
    }

    public async Task<AdminUserCreatedDto> CreateUserAsync(CreateUserRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.EmployeeId is not { } employeeId || employeeId == Guid.Empty)
            throw new RequestValidationException("An employee id is required.",
                new Dictionary<string, string[]> { ["employeeId"] = ["An employee id is required."] });

        var employee = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken)
            ?? throw new NotFoundException("The employee was not found.");

        if (await _db.UserAccounts.AsNoTracking().AnyAsync(u => u.EmployeeId == employee.Id, cancellationToken))
            throw new ConflictException("An account already exists for this employee.");

        var roles = await ResolveRolesAsync(request.Roles, cancellationToken);

        var userName = await GenerateUserNameAsync(employee, cancellationToken);
        var temporaryPassword = "Hrms!" + PasswordHasher.CreateSecureToken(12);
        var now = _clock.UtcNow;

        var account = new UserAccount
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            Email = employee.WorkEmail,
            PasswordHash = PasswordHasher.Hash(temporaryPassword),
            EmployeeId = employee.Id,
            Status = UserStatus.Active,
            MfaEnabled = false,
            FailedLoginCount = 0,
            MustChangePassword = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        foreach (var role in roles)
            account.UserRoles.Add(new UserRole { UserId = account.Id, RoleId = role.Id });

        _db.UserAccounts.Add(account);

        // FR-ADM-143: permission changes are logged. Never include the generated password.
        _audit.Add("admin.user_created", nameof(UserAccount), account.Id.ToString(), "Success",
            new
            {
                employeeNumber = employee.EmployeeNumber,
                userName,
                roles = roles.Select(r => r.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray()
            });

        await _db.SaveChangesAsync(cancellationToken);

        return new AdminUserCreatedDto
        {
            Id = account.Id,
            EmployeeId = account.EmployeeId,
            EmployeeNumber = employee.EmployeeNumber,
            DisplayName = employee.DisplayName,
            WorkEmail = employee.WorkEmail,
            Roles = roles.Select(r => r.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray(),
            Status = account.Status,
            MfaEnabled = account.MfaEnabled,
            LastLoginAt = account.LastLoginAt,
            SessionsActive = 0,
            TemporaryPassword = temporaryPassword
        };
    }

    public async Task UpdateRolesAsync(Guid userId, UpdateUserRolesRequest request,
        CancellationToken cancellationToken = default)
    {
        var account = await _db.UserAccounts.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException("The user was not found.");

        var roles = await ResolveRolesAsync(request.Roles, cancellationToken);
        var desired = roles.Select(r => r.Id).ToHashSet();

        var current = await _db.UserRoles.Include(ur => ur.Role)
            .Where(ur => ur.UserId == userId)
            .ToListAsync(cancellationToken);
        var currentById = current.ToDictionary(ur => ur.RoleId);

        var removed = current.Where(ur => !desired.Contains(ur.RoleId))
            .Select(ur => ur.Role!.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        var added = roles.Where(r => !currentById.ContainsKey(r.Id))
            .Select(r => r.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        foreach (var assignment in current.Where(ur => !desired.Contains(ur.RoleId)))
            _db.UserRoles.Remove(assignment);

        foreach (var role in roles.Where(r => !currentById.ContainsKey(r.Id)))
            _db.UserRoles.Add(new UserRole { UserId = userId, RoleId = role.Id });

        account.UpdatedAt = _clock.UtcNow;

        _audit.Add("admin.user_roles_updated", nameof(UserAccount), userId.ToString(), "Success",
            new
            {
                roles = roles.Select(r => r.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray(),
                added,
                removed
            });

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task SuspendUserAsync(Guid userId, string? reason, CancellationToken cancellationToken = default)
    {
        var account = await _db.UserAccounts.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException("The user was not found.");

        var previousStatus = account.Status;
        account.Status = UserStatus.Suspended;
        account.UpdatedAt = _clock.UtcNow;

        // BR-008: the account status is re-checked on every request, so access ends immediately.
        _audit.Add("admin.user_suspended", nameof(UserAccount), userId.ToString(), "Success",
            new { account.UserName, previousStatus, reason });

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task ActivateUserAsync(Guid userId, string? reason, CancellationToken cancellationToken = default)
    {
        var account = await _db.UserAccounts.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException("The user was not found.");

        var previousStatus = account.Status;
        account.Status = UserStatus.Active;
        account.FailedLoginCount = 0;
        account.AccessEndsOn = null;
        account.UpdatedAt = _clock.UtcNow;

        _audit.Add("admin.user_activated", nameof(UserAccount), userId.ToString(), "Success",
            new { account.UserName, previousStatus, reason });

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeSessionsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var account = await _db.UserAccounts.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException("The user was not found.");

        var now = _clock.UtcNow;
        var active = await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now)
            .ToListAsync(cancellationToken);

        foreach (var token in active) token.RevokedAt = now;

        _audit.Add("admin.user_sessions_revoked", nameof(UserAccount), userId.ToString(), "Success",
            new { account.UserName, revoked = active.Count });

        await _db.SaveChangesAsync(cancellationToken);
    }

    // ---------------------------------------------------------------- roles

    public async Task<IReadOnlyList<RoleListItem>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        return await _db.Roles.AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => new RoleListItem
            {
                Name = r.Name,
                Description = r.Description,
                IsSystem = r.IsSystem,
                UserCount = r.UserRoles.Count(),
                Permissions = r.RolePermissions.Select(p => p.PermissionCode).ToArray()
            })
            .ToListAsync(cancellationToken);
    }

    public async Task UpdateRolePermissionsAsync(string name, UpdateRolePermissionsRequest request,
        CancellationToken cancellationToken = default)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Name == name, cancellationToken)
            ?? throw new NotFoundException($"Role '{name}' was not found.");

        if (string.Equals(role.Name, Roles.SuperAdmin, StringComparison.Ordinal))
            throw new ForbiddenException("The SuperAdmin role's permissions cannot be modified.");

        var requested = (request.Permissions ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (requested.Contains(Permissions.All, StringComparer.Ordinal))
            throw new ForbiddenException("The wildcard permission '*' cannot be granted through this endpoint.");

        var unknown = requested.Where(p => !Permissions.AllCodes.Contains(p, StringComparer.Ordinal)).ToList();
        if (unknown.Count > 0)
            throw new RequestValidationException("One or more permissions are invalid.",
                new Dictionary<string, string[]>
                {
                    ["permissions"] = unknown.Select(u => $"Unknown permission '{u}'.").ToArray()
                });

        var existing = await _db.RolePermissions.Where(p => p.RoleId == role.Id).ToListAsync(cancellationToken);
        var existingCodes = existing.Select(p => p.PermissionCode).ToHashSet(StringComparer.Ordinal);
        var requestedCodes = requested.ToHashSet(StringComparer.Ordinal);

        var added = requested.Where(p => !existingCodes.Contains(p)).OrderBy(p => p, StringComparer.Ordinal).ToList();
        var removed = existing.Select(p => p.PermissionCode)
            .Where(c => !requestedCodes.Contains(c))
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        foreach (var permission in existing.Where(p => !requestedCodes.Contains(p.PermissionCode)))
            _db.RolePermissions.Remove(permission);

        foreach (var code in added)
            _db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionCode = code });

        _audit.Add("admin.role_permissions_updated", nameof(Role), role.Id.ToString(), "Success",
            new { role = role.Name, added, removed });

        await _db.SaveChangesAsync(cancellationToken);
    }

    // ---------------------------------------------------------------- configuration

    public async Task<IReadOnlyList<ConfigurationListItem>> GetConfigurationAsync(CancellationToken cancellationToken = default)
    {
        return await _db.Set<SystemConfiguration>().AsNoTracking()
            .OrderBy(c => c.Key)
            .Select(c => new ConfigurationListItem
            {
                Key = c.Key,
                Value = c.Value,
                Type = c.Type,
                Description = c.Description,
                UpdatedAt = c.UpdatedAt,
                UpdatedByName = c.UpdatedByUser == null
                    ? null
                    : (c.UpdatedByUser.Employee != null ? c.UpdatedByUser.Employee.DisplayName : c.UpdatedByUser.UserName)
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<ConfigurationListItem> UpdateConfigurationAsync(string key, UpdateConfigurationRequest request,
        CancellationToken cancellationToken = default)
    {
        var entry = await _db.Set<SystemConfiguration>()
            .FirstOrDefaultAsync(c => c.Key == key, cancellationToken)
            ?? throw new NotFoundException($"Configuration key '{key}' was not found.");

        var value = request.Value ?? string.Empty;
        ValidateConfigurationValue(entry, value);

        var previousValue = entry.Value;
        entry.Value = value;
        entry.UpdatedAt = _clock.UtcNow;
        entry.UpdatedByUserId = _currentUser.RequireUserId();

        // BR-012: the new value applies from now on; the previous value is preserved in the audit trail.
        _audit.Add("admin.configuration_updated", nameof(SystemConfiguration), entry.Key, "Success",
            new { key = entry.Key, previousValue, value, applies = "prospective" });

        await _db.SaveChangesAsync(cancellationToken);

        return new ConfigurationListItem
        {
            Key = entry.Key,
            Value = entry.Value,
            Type = entry.Type,
            Description = entry.Description,
            UpdatedAt = entry.UpdatedAt,
            UpdatedByName = _currentUser.DisplayName
        };
    }

    // ---------------------------------------------------------------- audit log (read-only, FR-ADM-153)

    public async Task<PagedResult<AuditListItem>> GetAuditAsync(string? actor, string? action, string? entityType,
        string? entityId, DateOnly? from, DateOnly? to, PageRequest page, CancellationToken cancellationToken = default)
    {
        if (from is not null && to is not null && from.Value > to.Value)
            throw new RequestValidationException("The date range is not valid.",
                new Dictionary<string, string[]> { ["from"] = ["'from' must be on or before 'to'."] });

        IQueryable<AuditEvent> query = _db.AuditEvents.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(actor))
        {
            var term = actor.Trim();
            query = query.Where(a => a.ActorName.Contains(term) ||
                                     (a.ActorEmployeeNumber != null && a.ActorEmployeeNumber.Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            var term = action.Trim();
            query = query.Where(a => a.Action.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(entityType))
        {
            var term = entityType.Trim();
            query = query.Where(a => a.EntityType == term);
        }

        if (!string.IsNullOrWhiteSpace(entityId))
        {
            var term = entityId.Trim();
            query = query.Where(a => a.EntityId == term);
        }

        if (from is { } fromDate)
            query = query.Where(a => a.Timestamp >= fromDate.ToDateTime(TimeOnly.MinValue));

        if (to is { } toDate)
            query = query.Where(a => a.Timestamp < toDate.AddDays(1).ToDateTime(TimeOnly.MinValue));

        var result = await query
            .OrderByDescending(a => a.Timestamp)
            .ThenByDescending(a => a.Id)
            .ToPagedAsync(page, a => new AuditListItem
            {
                Id = a.Id,
                ActorName = a.ActorName,
                ActorEmployeeNumber = a.ActorEmployeeNumber,
                Action = a.Action,
                EntityType = a.EntityType,
                EntityId = a.EntityId,
                Outcome = a.Outcome,
                CorrelationId = a.CorrelationId,
                IpAddress = a.IpAddress,
                Timestamp = a.Timestamp,
                DetailsJson = a.Details
            }, cancellationToken);

        // Searching the audit trail is itself a sensitive read (FR-ADM-152/153).
        await _audit.RecordAsync("admin.audit_viewed", nameof(AuditEvent), null, "Success",
            new { actor, action, entityType, entityId, from, to, page = page.Page, pageSize = page.PageSize, result.Total },
            cancellationToken: cancellationToken);

        return result;
    }

    // ---------------------------------------------------------------- operations (FR-ADM-004/154)

    public Task<OperationsDto> GetOperationsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var startedAtUtc = Process.GetCurrentProcess().StartTime.ToUniversalTime();
        var uptimeMinutes = (int)Math.Floor(Math.Max(0, (_clock.UtcNow - startedAtUtc).TotalMinutes));

        double? dbSizeMb = null;
        if (_db.Database.IsRelational())
        {
            try
            {
                var dataSource = _db.Database.GetDbConnection().DataSource;
                if (!string.IsNullOrWhiteSpace(dataSource) && File.Exists(dataSource))
                    dbSizeMb = Math.Round(new FileInfo(dataSource).Length / (1024d * 1024d), 2);
            }
            catch (Exception ex) when (ex is NotSupportedException or IOException
                                       or UnauthorizedAccessException or ArgumentException)
            {
                // Provider without a local file (e.g. SQL Server) or unreadable file — reported as null.
            }
        }

        var backgroundJobs = _hostedServices
            .Select(service => new BackgroundJobDto
            {
                Name = service.GetType().Name,
                LastRunAt = startedAtUtc,
                Status = "Running"
            })
            .OrderBy(job => job.Name, StringComparer.Ordinal)
            .ToList();

        return Task.FromResult(new OperationsDto
        {
            BackgroundJobs = backgroundJobs,
            NotificationFailures24h = null, // owned by the Notifications module's store — not readable here.
            IntegrationErrors24h = null,   // owned by the Integrations module's store — not readable here.
            DbSizeMb = dbSizeMb,
            UptimeMinutes = uptimeMinutes
        });
    }

    // ---------------------------------------------------------------- helpers

    private async Task<List<Role>> ResolveRolesAsync(IEnumerable<string?>? roleNames, CancellationToken cancellationToken)
    {
        var requested = (roleNames ?? [])
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (requested.Count == 0) return [];

        var roles = await _db.Roles.AsNoTracking()
            .Where(r => requested.Contains(r.Name))
            .ToListAsync(cancellationToken);

        var missing = requested.Except(roles.Select(r => r.Name), StringComparer.Ordinal).ToList();
        if (missing.Count > 0)
            throw new RequestValidationException("One or more roles are invalid.",
                new Dictionary<string, string[]>
                {
                    ["roles"] = missing.Select(m => $"Role '{m}' does not exist.").ToArray()
                });

        return roles;
    }

    private async Task<string> GenerateUserNameAsync(Employee employee, CancellationToken cancellationToken)
    {
        var localPart = employee.WorkEmail.Contains('@')
            ? employee.WorkEmail[..employee.WorkEmail.IndexOf('@')]
            : employee.EmployeeNumber;

        var baseName = Sanitize(localPart);
        if (baseName.Length == 0) baseName = Sanitize(employee.EmployeeNumber);
        if (baseName.Length == 0) baseName = "user";

        var candidate = baseName;
        var suffix = 2;
        while (await _db.UserAccounts.AsNoTracking().AnyAsync(u => u.UserName == candidate, cancellationToken))
        {
            candidate = $"{baseName}{suffix}";
            suffix++;
        }

        return candidate;

        static string Sanitize(string value) => new(value.ToLowerInvariant()
            .Where(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '-')
            .ToArray());
    }

    private static void ValidateConfigurationValue(SystemConfiguration entry, string value)
    {
        Dictionary<string, string[]> Error(string message) => new() { ["value"] = [message] };

        switch (entry.Type)
        {
            case "Int":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    throw new RequestValidationException("The value is not valid for this key.", Error("The value must be a whole number."));
                break;
            case "Bool":
                if (!bool.TryParse(value, out _))
                    throw new RequestValidationException("The value is not valid for this key.", Error("The value must be 'true' or 'false'."));
                break;
            case "Date":
                if (!DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                    throw new RequestValidationException("The value is not valid for this key.", Error("The value must be a date (YYYY-MM-DD)."));
                break;
            case "Json":
                try
                {
                    using var _ = JsonDocument.Parse(value);
                }
                catch (JsonException)
                {
                    throw new RequestValidationException("The value is not valid for this key.", Error("The value must be valid JSON."));
                }
                break;
        }
    }
}
