using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Wintech.Hrms.Api.Common.Configuration;
using Wintech.Hrms.Api.Common.Registration;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Identity;
using Wintech.Hrms.Api.Security;

namespace Wintech.Hrms.Api.Modules.Auth;

public sealed record IssuedAccessToken(string AccessToken, int ExpiresIn, Guid SessionId);

/// <summary>Raw refresh token handed to the client plus its persisted (hashed) session row.</summary>
public sealed record IssuedRefreshToken(string RawToken, RefreshToken Session);

/// <summary>Issues/validates JWT access &amp; MFA tokens and manages rotating refresh sessions (FR-EMP-004/005).</summary>
public interface ITokenService : IAppService
{
    IssuedAccessToken CreateAccessToken(UserAccount user, IEnumerable<string> roles, Guid sessionId);

    string CreateMfaToken(UserAccount user);

    /// <summary>Returns the user id when the MFA token is valid, otherwise null.</summary>
    Guid? ValidateMfaToken(string mfaToken);

    Task<IssuedRefreshToken> IssueRefreshTokenAsync(UserAccount user, string? deviceName, string? ipAddress,
        Guid? familyId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates and rotates a refresh token. Returns the new pair, or null when the token is
    /// unknown, expired or revoked. Reuse of an already-rotated token revokes the whole family.
    /// </summary>
    Task<IssuedRefreshToken?> RotateAsync(string rawRefreshToken, string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<bool> RevokeAsync(string rawRefreshToken, CancellationToken cancellationToken = default);

    Task RevokeAllForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<RefreshToken?> FindAsync(string rawRefreshToken, CancellationToken cancellationToken = default);
}

public sealed class TokenService : ITokenService
{
    private readonly JwtOptions _options;
    private readonly AppDbContext _db;
    private readonly IClock _clock;

    public TokenService(JwtOptions options, AppDbContext db, IClock clock)
    {
        _options = options;
        _db = db;
        _clock = clock;
    }

    private SigningCredentials SigningCredentials =>
        new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)), SecurityAlgorithms.HmacSha256);

    public IssuedAccessToken CreateAccessToken(UserAccount user, IEnumerable<string> roles, Guid sessionId)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new("emp", user.EmployeeId?.ToString() ?? string.Empty),
            new("name", user.Employee?.DisplayName ?? user.UserName),
            new("sid", sessionId.ToString())
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: _clock.UtcNow,
            expires: _clock.UtcNow.AddMinutes(_options.AccessTokenMinutes),
            signingCredentials: SigningCredentials);

        return new IssuedAccessToken(new JwtSecurityTokenHandler().WriteToken(token),
            _options.AccessTokenMinutes * 60, sessionId);
    }

    public string CreateMfaToken(UserAccount user)
    {
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim("purpose", "mfa")
            ],
            notBefore: _clock.UtcNow,
            expires: _clock.UtcNow.AddMinutes(_options.MfaTokenMinutes),
            signingCredentials: SigningCredentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public Guid? ValidateMfaToken(string mfaToken)
    {
        if (string.IsNullOrWhiteSpace(mfaToken)) return null;

        var handler = new JwtSecurityTokenHandler { InboundClaimTypeMap = new Dictionary<string, string>() };
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _options.Issuer,
            ValidateAudience = true,
            ValidAudience = _options.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        try
        {
            var principal = handler.ValidateToken(mfaToken, parameters, out _);
            if (principal.FindFirst("purpose")?.Value != "mfa") return null;
            var sub = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            return Guid.TryParse(sub, out var userId) ? userId : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<IssuedRefreshToken> IssueRefreshTokenAsync(UserAccount user, string? deviceName,
        string? ipAddress, Guid? familyId = null, CancellationToken cancellationToken = default)
    {
        var rawToken = PasswordHasher.CreateSecureToken();
        var session = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = PasswordHasher.HashToken(rawToken),
            FamilyId = familyId ?? Guid.NewGuid(),
            DeviceName = string.IsNullOrWhiteSpace(deviceName)
                ? "Unknown device"
                : deviceName[..Math.Min(deviceName.Length, 200)],
            IpAddress = ipAddress,
            CreatedAt = _clock.UtcNow,
            ExpiresAt = _clock.UtcNow.AddDays(_options.RefreshTokenDays)
        };

        _db.RefreshTokens.Add(session);
        return new IssuedRefreshToken(rawToken, session);
    }

    public async Task<IssuedRefreshToken?> RotateAsync(string rawRefreshToken, string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var existing = await FindAsync(rawRefreshToken, cancellationToken);
        if (existing is null) return null;

        if (existing.RevokedAt is not null)
        {
            // Reuse of an already-rotated token: treat the family as compromised (FR-EMP-005).
            var familyMembers = await _db.RefreshTokens
                .Where(t => t.FamilyId == existing.FamilyId && t.RevokedAt == null)
                .ToListAsync(cancellationToken);
            foreach (var member in familyMembers) member.RevokedAt = _clock.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            return null;
        }

        if (existing.ExpiresAt <= _clock.UtcNow) return null;

        var rotated = await IssueRefreshTokenAsync(existing.User!, existing.DeviceName, ipAddress,
            existing.FamilyId, cancellationToken);
        existing.RevokedAt = _clock.UtcNow;
        existing.LastUsedAt = _clock.UtcNow;
        existing.ReplacedByTokenHash = rotated.Session.TokenHash;
        rotated.Session.DeviceName = existing.DeviceName ?? rotated.Session.DeviceName;
        return rotated;
    }

    public async Task<bool> RevokeAsync(string rawRefreshToken, CancellationToken cancellationToken = default)
    {
        var token = await FindAsync(rawRefreshToken, cancellationToken);
        if (token is not { RevokedAt: null }) return false;

        token.RevokedAt = _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var tokens = await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var token in tokens) token.RevokedAt = _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public Task<RefreshToken?> FindAsync(string rawRefreshToken, CancellationToken cancellationToken = default)
    {
        var hash = PasswordHasher.HashToken(rawRefreshToken);
        return _db.RefreshTokens.Include(t => t.User)
            .ThenInclude(u => u!.Employee)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
    }
}
