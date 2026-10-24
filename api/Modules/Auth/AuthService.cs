using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wintech.Hrms.Api.Common.Auditing;
using Wintech.Hrms.Api.Common.Configuration;
using Wintech.Hrms.Api.Common.CurrentUser;
using Wintech.Hrms.Api.Common.ErrorHandling;
using Wintech.Hrms.Api.Common.Registration;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Identity;
using Wintech.Hrms.Api.Security;

namespace Wintech.Hrms.Api.Modules.Auth;

/// <summary>Sign-in, MFA, session/refresh lifecycle and credential recovery (FR-EMP-001..007).</summary>
public interface IAuthService : IAppService
{
    Task<LoginResponse> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken cancellationToken = default);
    Task<LoginResponse> VerifyMfaAsync(MfaVerifyRequest request, string? ipAddress, CancellationToken cancellationToken = default);
    Task<LoginResponse> RefreshAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken = default);
    Task LogoutAsync(string? refreshToken, Guid? actorUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SessionDto>> GetSessionsAsync(Guid userId, Guid? currentSessionId, CancellationToken cancellationToken = default);
    Task RevokeSessionAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default);
    Task ForgotPasswordAsync(string username, CancellationToken cancellationToken = default);
    Task ResetPasswordAsync(string token, string newPassword, CancellationToken cancellationToken = default);
    Task ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default);
    Task<MfaSetupResponse> SetupMfaAsync(Guid userId, CancellationToken cancellationToken = default);
    Task EnableMfaAsync(Guid userId, string code, CancellationToken cancellationToken = default);
    Task DisableMfaAsync(Guid userId, string code, CancellationToken cancellationToken = default);
}

public sealed class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly ITokenService _tokens;
    private readonly IAuditService _audit;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly SecurityOptions _security;
    private readonly ILogger<AuthService> _logger;

    public AuthService(AppDbContext db, ITokenService tokens, IAuditService audit, ICurrentUser currentUser,
        IClock clock, IOptions<SecurityOptions> security, ILogger<AuthService> logger)
    {
        _db = db;
        _tokens = tokens;
        _audit = audit;
        _currentUser = currentUser;
        _clock = clock;
        _security = security.Value;
        _logger = logger;
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var user = await FindLoginUserAsync(request.Username, cancellationToken);

        if (user is null || !PasswordHasher.Verify(request.Password, user.PasswordHash))
        {
            // Never reveal whether the identifier exists (FR-EMP-002): identical generic 401.
            if (user is not null)
            {
                user.FailedLoginCount++;
                user.UpdatedAt = _clock.UtcNow;
                _audit.Add("auth.login_failed", nameof(UserAccount), user.Id.ToString(), "Failure",
                    new { request.Username, user.FailedLoginCount }, ActorFor(user));
                await _db.SaveChangesAsync(cancellationToken);
            }
            else
            {
                await _audit.RecordAsync("auth.login_failed", nameof(UserAccount), null, "Failure",
                    new { request.Username }, cancellationToken: cancellationToken);
            }

            throw new UnauthorizedException("Invalid username or password.");
        }

        if (user.Status != UserStatus.Active)
            throw new ApiException(403, "Account not active",
                "This account is suspended or terminated. Contact HR if you believe this is an error.");
        if (user.AccessEndsOn is { } ends && DateOnly.FromDateTime(_clock.UtcNow) > ends)
            throw new ApiException(403, "Account not active",
                "Your access has ended. Contact HR if you believe this is an error.");

        if (user.MfaEnabled)
        {
            _audit.Add("auth.mfa_challenge", nameof(UserAccount), user.Id.ToString(), "Success",
                new { request.DeviceName }, ActorFor(user));
            await _db.SaveChangesAsync(cancellationToken);
            return new LoginResponse { MfaRequired = true, MfaToken = _tokens.CreateMfaToken(user) };
        }

        user.FailedLoginCount = 0;
        user.LastLoginAt = _clock.UtcNow;
        user.UpdatedAt = _clock.UtcNow;
        _audit.Add("auth.login", nameof(UserAccount), user.Id.ToString(), "Success",
            new { request.DeviceName, Ip = ipAddress }, ActorFor(user));

        return await IssueSessionAsync(user, request.DeviceName, ipAddress, cancellationToken);
    }

    public async Task<LoginResponse> VerifyMfaAsync(MfaVerifyRequest request, string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var userId = _tokens.ValidateMfaToken(request.MfaToken);
        if (userId is null)
            throw new UnauthorizedException("The MFA challenge has expired. Sign in again.");

        var user = await LoadUserAsync(userId.Value, cancellationToken);
        if (user is null) throw new UnauthorizedException("Invalid MFA challenge.");
        if (user.Status != UserStatus.Active)
            throw new ApiException(403, "Account not active", "This account is not active.");
        if (user.MfaSecret is null || !Totp.Verify(user.MfaSecret, request.Code))
        {
            _audit.Add("auth.mfa_failed", nameof(UserAccount), user.Id.ToString(), "Failure", null, ActorFor(user));
            await _db.SaveChangesAsync(cancellationToken);
            throw new UnauthorizedException("Invalid MFA code.");
        }

        user.FailedLoginCount = 0;
        user.LastLoginAt = _clock.UtcNow;
        user.UpdatedAt = _clock.UtcNow;
        _audit.Add("auth.login", nameof(UserAccount), user.Id.ToString(), "Success",
            new { Mfa = true, Ip = ipAddress }, ActorFor(user));

        return await IssueSessionAsync(user, "MFA device", ipAddress, cancellationToken);
    }

    public async Task<LoginResponse> RefreshAsync(string refreshToken, string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var rotated = await _tokens.RotateAsync(refreshToken, ipAddress, cancellationToken);
        if (rotated is null)
        {
            await _audit.RecordAsync("auth.refresh_failed", nameof(UserAccount), null, "Failure",
                new { Ip = ipAddress }, cancellationToken: cancellationToken);
            throw new UnauthorizedException("Your session has expired. Please sign in again.");
        }

        var user = await LoadUserAsync(rotated.Session.UserId, cancellationToken);
        if (user is null) throw new UnauthorizedException("Your session has expired. Please sign in again.");

        if (user.Status != UserStatus.Active ||
            (user.AccessEndsOn is { } ends && DateOnly.FromDateTime(_clock.UtcNow) > ends))
        {
            await _tokens.RevokeAllForUserAsync(user.Id, cancellationToken);
            throw new ApiException(403, "Account not active", "This account is no longer active.");
        }

        _audit.Add("auth.refresh", nameof(UserAccount), user.Id.ToString(), "Success",
            new { Session = rotated.Session.Id }, ActorFor(user));
        var roles = GetUserRoles(user);
        var access = _tokens.CreateAccessToken(user, roles, rotated.Session.Id);
        await _db.SaveChangesAsync(cancellationToken);

        return new LoginResponse
        {
            AccessToken = access.AccessToken,
            RefreshToken = rotated.RawToken,
            ExpiresIn = access.ExpiresIn,
            User = BuildSummary(user, roles)
        };
    }

    public async Task LogoutAsync(string? refreshToken, Guid? actorUserId, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(refreshToken))
            await _tokens.RevokeAsync(refreshToken, cancellationToken);

        await _audit.RecordAsync("auth.logout", nameof(UserAccount), actorUserId?.ToString(), "Success",
            actorOverride: actorUserId is { } id ? new AuditActor(id, null, null) : null,
            cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<SessionDto>> GetSessionsAsync(Guid userId, Guid? currentSessionId,
        CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var sessions = await _db.RefreshTokens.AsNoTracking()
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);

        return sessions.Select(t => new SessionDto
        {
            Id = t.Id,
            DeviceName = t.DeviceName,
            IpAddress = t.IpAddress,
            CreatedAt = t.CreatedAt,
            LastUsedAt = t.LastUsedAt,
            Current = currentSessionId is not null && t.Id == currentSessionId
        }).ToList();
    }

    public async Task RevokeSessionAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await _db.RefreshTokens
            .FirstOrDefaultAsync(t => t.Id == sessionId && t.UserId == userId, cancellationToken);
        // Scoped to the caller's own sessions: unknown ids are indistinguishable from others' (404).
        if (session is null) throw new NotFoundException("The session was not found.");
        if (session.RevokedAt is null) session.RevokedAt = _clock.UtcNow;

        _audit.Add("auth.session_revoked", nameof(RefreshToken), sessionId.ToString(), "Success");
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task ForgotPasswordAsync(string username, CancellationToken cancellationToken = default)
    {
        var user = await FindLoginUserAsync(username, cancellationToken);
        if (user is null || user.Status != UserStatus.Active)
        {
            // Same response either way — no account enumeration (FR-EMP-002).
            await _audit.RecordAsync("auth.password_reset_requested", nameof(UserAccount), null, "Ignored",
                new { Username = username }, cancellationToken: cancellationToken);
            return;
        }

        var rawToken = PasswordHasher.CreateSecureToken(32);
        _db.PasswordResetTokens.Add(new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = PasswordHasher.HashToken(rawToken),
            CreatedAt = _clock.UtcNow,
            ExpiresAt = _clock.UtcNow.AddMinutes(30)
        });

        _audit.Add("auth.password_reset_requested", nameof(UserAccount), user.Id.ToString(), "Success",
            actorOverride: ActorFor(user));
        await _db.SaveChangesAsync(cancellationToken);

        // Dev aid only: production would send this through the configured email provider (section 10).
        _logger.LogWarning("Password reset token for {UserName}: {Token} (valid 30 minutes, development only)",
            user.UserName, rawToken);
    }

    public async Task ResetPasswordAsync(string token, string newPassword, CancellationToken cancellationToken = default)
    {
        ValidatePasswordStrength(newPassword, "newPassword");

        var hash = PasswordHasher.HashToken(token);
        var record = await _db.PasswordResetTokens.Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (record is null || record.UsedAt is not null || record.ExpiresAt <= _clock.UtcNow)
            throw new RequestValidationException("The reset link is invalid or has expired.",
                new() { ["token"] = ["The reset link is invalid or has expired."] });

        var user = record.User!;
        user.PasswordHash = PasswordHasher.Hash(newPassword);
        user.UpdatedAt = _clock.UtcNow;
        user.MustChangePassword = false;
        record.UsedAt = _clock.UtcNow;

        _audit.Add("auth.password_reset", nameof(UserAccount), user.Id.ToString(), "Success",
            actorOverride: ActorFor(user));
        await _db.SaveChangesAsync(cancellationToken);

        await _tokens.RevokeAllForUserAsync(user.Id, cancellationToken);
        _logger.LogInformation("Password reset completed for {UserName}; all sessions revoked.", user.UserName);
    }

    public async Task ChangePasswordAsync(Guid userId, string currentPassword, string newPassword,
        CancellationToken cancellationToken = default)
    {
        ValidatePasswordStrength(newPassword, "newPassword");

        var user = await LoadUserAsync(userId, cancellationToken)
                   ?? throw new UnauthorizedException();

        if (!PasswordHasher.Verify(currentPassword, user.PasswordHash))
            throw new RequestValidationException("The current password is incorrect.",
                new() { ["currentPassword"] = ["Current password is incorrect."] });

        if (string.Equals(currentPassword, newPassword, StringComparison.Ordinal))
            throw new RequestValidationException("The new password must be different.",
                new() { ["newPassword"] = ["The new password must be different from the current password."] });

        user.PasswordHash = PasswordHasher.Hash(newPassword);
        user.UpdatedAt = _clock.UtcNow;
        _audit.Add("auth.password_changed", nameof(UserAccount), user.Id.ToString(), "Success",
            actorOverride: ActorFor(user));
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<MfaSetupResponse> SetupMfaAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await LoadUserAsync(userId, cancellationToken) ?? throw new NotFoundException("Account not found.");
        var secret = Totp.GenerateSecret();
        user.MfaSecret = secret;
        user.UpdatedAt = _clock.UtcNow;
        _audit.Add("auth.mfa_setup_started", nameof(UserAccount), user.Id.ToString(), "Success",
            actorOverride: ActorFor(user));
        await _db.SaveChangesAsync(cancellationToken);

        return new MfaSetupResponse(secret, Totp.BuildOtpAuthUri(secret, user.UserName));
    }

    public async Task EnableMfaAsync(Guid userId, string code, CancellationToken cancellationToken = default)
    {
        var user = await LoadUserAsync(userId, cancellationToken) ?? throw new NotFoundException("Account not found.");
        if (user.MfaSecret is null)
            throw new RequestValidationException("Run MFA setup first.",
                new() { ["code"] = ["MFA setup has not been started."] });
        if (!Totp.Verify(user.MfaSecret, code))
            throw new RequestValidationException("The code is not valid.",
                new() { ["code"] = ["The authenticator code is not valid. Check the time on your device."] });

        user.MfaEnabled = true;
        user.UpdatedAt = _clock.UtcNow;
        _audit.Add("auth.mfa_enabled", nameof(UserAccount), user.Id.ToString(), "Success",
            actorOverride: ActorFor(user));
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DisableMfaAsync(Guid userId, string code, CancellationToken cancellationToken = default)
    {
        var user = await LoadUserAsync(userId, cancellationToken) ?? throw new NotFoundException("Account not found.");
        if (!user.MfaEnabled) return;
        if (user.MfaSecret is null || !Totp.Verify(user.MfaSecret, code))
            throw new RequestValidationException("The code is not valid.",
                new() { ["code"] = ["The authenticator code is not valid."] });

        user.MfaEnabled = false;
        user.MfaSecret = null;
        user.UpdatedAt = _clock.UtcNow;
        _audit.Add("auth.mfa_disabled", nameof(UserAccount), user.Id.ToString(), "Success",
            actorOverride: ActorFor(user));
        await _db.SaveChangesAsync(cancellationToken);
    }

    // ------------------------------------------------------------------ helpers

    private async Task<LoginResponse> IssueSessionAsync(UserAccount user, string? deviceName, string? ipAddress,
        CancellationToken cancellationToken)
    {
        var roles = GetUserRoles(user);
        var refresh = await _tokens.IssueRefreshTokenAsync(user, deviceName, ipAddress, cancellationToken: cancellationToken);
        var access = _tokens.CreateAccessToken(user, roles, refresh.Session.Id);
        await _db.SaveChangesAsync(cancellationToken);

        return new LoginResponse
        {
            AccessToken = access.AccessToken,
            RefreshToken = refresh.RawToken,
            ExpiresIn = access.ExpiresIn,
            MfaRequired = false,
            User = BuildSummary(user, roles)
        };
    }

    private static string[] GetUserRoles(UserAccount user) =>
        user.UserRoles.Where(r => r.Role is not null).Select(r => r.Role!.Name).Distinct().ToArray();

    private static CurrentUserSummary BuildSummary(UserAccount user, string[] roles)
    {
        var permissions = user.UserRoles
            .Where(r => r.Role is not null)
            .SelectMany(r => r.Role!.RolePermissions)
            .Select(p => p.PermissionCode)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var employee = user.Employee;
        return new CurrentUserSummary
        {
            Id = user.Id,
            EmployeeId = user.EmployeeId,
            EmployeeNumber = employee?.EmployeeNumber ?? string.Empty,
            DisplayName = employee?.DisplayName ?? user.UserName,
            WorkEmail = employee?.WorkEmail ?? user.Email,
            Designation = employee?.Designation?.Name,
            Department = employee?.Department?.Name,
            Roles = roles,
            Permissions = permissions
        };
    }

    private static AuditActor ActorFor(UserAccount user) =>
        new(user.Id, user.Employee?.EmployeeNumber, user.Employee?.DisplayName ?? user.UserName);

    private void ValidatePasswordStrength(string password, string field)
    {
        var errors = new Dictionary<string, string[]>();
        var minLength = Math.Max(8, _security.PasswordMinLength);

        if (string.IsNullOrEmpty(password) || password.Length < minLength)
            errors[field] = [$"Password must be at least {minLength} characters."];
        else if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            errors[field] = ["Password must contain at least one letter and one number."];

        if (errors.Count > 0)
            throw new RequestValidationException("The password does not meet the password policy.", errors);
    }

    private async Task<UserAccount?> FindLoginUserAsync(string? username, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(username)) return null;
        var term = username.Trim().ToLowerInvariant();

        return await LoadBaseQuery()
            .FirstOrDefaultAsync(u =>
                u.UserName == term ||
                (u.Email != null && u.Email!.ToLower() == term) ||
                (u.Employee != null && u.Employee!.EmployeeNumber.ToLower() == term), cancellationToken);
    }

    private Task<UserAccount?> LoadUserAsync(Guid userId, CancellationToken cancellationToken) =>
        LoadBaseQuery().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)!;

    private IQueryable<UserAccount> LoadBaseQuery() =>
        _db.UserAccounts
            .Include(u => u.Employee).ThenInclude(e => e!.Designation)
            .Include(u => u.Employee).ThenInclude(e => e!.Department)
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role!).ThenInclude(r => r.RolePermissions);
}
