using Wintech.Hrms.Api.Domain.Employees;

namespace Wintech.Hrms.Api.Domain.Identity;

public enum UserStatus
{
    Active = 0,
    Suspended = 1,
    Terminated = 2,
    Pending = 3
}

/// <summary>Login identity mapped to an employee record (FR-ADM-140). Passwords are hashed, never stored.</summary>
public class UserAccount
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public Guid? EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public UserStatus Status { get; set; } = UserStatus.Active;
    /// <summary>Access ends on this date (inclusive) — BR-008 effective-time revocation.</summary>
    public DateOnly? AccessEndsOn { get; set; }
    public bool MfaEnabled { get; set; }
    public string? MfaSecret { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public bool MustChangePassword { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<UserRole> UserRoles { get; set; } = [];
    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
}

public class Role
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsSystem { get; set; }
    public ICollection<UserRole> UserRoles { get; set; } = [];
    public ICollection<RolePermission> RolePermissions { get; set; } = [];
}

public class UserRole
{
    public Guid UserId { get; set; }
    public UserAccount? User { get; set; }
    public Guid RoleId { get; set; }
    public Role? Role { get; set; }
}

public class RolePermission
{
    public Guid RoleId { get; set; }
    public Role? Role { get; set; }
    public string PermissionCode { get; set; } = string.Empty;
}

/// <summary>Rotating refresh token / device session (FR-EMP-004/005). Only the SHA-256 hash is stored.</summary>
public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public UserAccount? User { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    /// <summary>Groups rotations of the same login; reuse of a rotated token revokes the family.</summary>
    public Guid FamilyId { get; set; }
    public string? DeviceName { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? ReplacedByTokenHash { get; set; }

    public bool IsActive => RevokedAt is null && ExpiresAt > DateTime.UtcNow;
}

public class PasswordResetToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public UserAccount? User { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
}
