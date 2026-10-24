namespace Wintech.Hrms.Api.Modules.Auth;

public sealed record LoginRequest(string Username, string Password, string? DeviceName);

public sealed record MfaVerifyRequest(string MfaToken, string Code);

public sealed record RefreshRequest(string RefreshToken);

public sealed record LogoutRequest(string? RefreshToken);

public sealed record ForgotPasswordRequest(string Username);

public sealed record ResetPasswordRequest(string Token, string NewPassword);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record MfaEnableRequest(string Code);

public sealed record MfaSetupResponse(string Secret, string OtpAuthUri);

public sealed class CurrentUserSummary
{
    public Guid Id { get; init; }
    public Guid? EmployeeId { get; init; }
    public string EmployeeNumber { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? WorkEmail { get; init; }
    public string? Designation { get; init; }
    public string? Department { get; init; }
    public string[] Roles { get; init; } = [];
    public string[] Permissions { get; init; } = [];
}

public sealed class LoginResponse
{
    public string? AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public int ExpiresIn { get; init; }
    public bool MfaRequired { get; init; }
    public string? MfaToken { get; init; }
    public CurrentUserSummary? User { get; init; }
}

public sealed class SessionDto
{
    public Guid Id { get; init; }
    public string? DeviceName { get; init; }
    public string? IpAddress { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? LastUsedAt { get; init; }
    public bool Current { get; init; }
}
