using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wintech.Hrms.Api.Common.RateLimiting;

namespace Wintech.Hrms.Api.Modules.Auth;

[ApiController]
[Route("api/v1/auth")]
[EnableRateLimiting(RateLimitingExtensions.AuthPolicy)]
[Produces("application/json")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _auth;

    public AuthController(IAuthService auth) => _auth = auth;

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    private Guid? CurrentSessionId
    {
        get
        {
            var value = User.FindFirst("sid")?.Value
                        ?? User.Claims.FirstOrDefault(c => c.Type.EndsWith("/sid", StringComparison.Ordinal))?.Value;
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    private Guid CurrentUserId =>
        Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value, out var id)
            ? id
            : Guid.Empty;

    /// <summary>Sign in with employee number, username or work email (FR-EMP-001).</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
        => Ok(await _auth.LoginAsync(request, ClientIp, cancellationToken));

    /// <summary>Complete sign-in after an MFA challenge (FR-EMP-003).</summary>
    [HttpPost("mfa/verify")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> VerifyMfa([FromBody] MfaVerifyRequest request, CancellationToken cancellationToken)
        => Ok(await _auth.VerifyMfaAsync(request, ClientIp, cancellationToken));

    /// <summary>Rotate the refresh token and obtain a new access token (FR-EMP-005).</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> Refresh([FromBody] RefreshRequest request, CancellationToken cancellationToken)
        => Ok(await _auth.RefreshAsync(request.RefreshToken, ClientIp, cancellationToken));

    /// <summary>Revoke the presented refresh session; idempotent (FR-EMP-004).</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest request, CancellationToken cancellationToken)
    {
        var actorUserId = User.Identity?.IsAuthenticated == true && CurrentUserId != Guid.Empty
            ? CurrentUserId
            : (Guid?)null;

        await _auth.LogoutAsync(request.RefreshToken, actorUserId, cancellationToken);
        return NoContent();
    }

    /// <summary>List this user's active device sessions (FR-EMP-004).</summary>
    [HttpGet("sessions")]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<SessionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SessionDto>>> Sessions(CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        return Ok(await _auth.GetSessionsAsync(userId, CurrentSessionId, cancellationToken));
    }

    /// <summary>Remotely revoke another device's session (FR-EMP-004).</summary>
    [HttpDelete("sessions/{id:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeSession(Guid id, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        await _auth.RevokeSessionAsync(userId, id, cancellationToken);
        return NoContent();
    }

    /// <summary>Start password recovery — always returns 202, never reveals existence (FR-EMP-002).</summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        await _auth.ForgotPasswordAsync(request.Username, cancellationToken);
        return Accepted();
    }

    /// <summary>Complete password recovery with a one-time token (FR-EMP-002).</summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        await _auth.ResetPasswordAsync(request.Token, request.NewPassword, cancellationToken);
        return NoContent();
    }

    /// <summary>Change the caller's password while signed in.</summary>
    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        await _auth.ChangePasswordAsync(userId, request.CurrentPassword, request.NewPassword, cancellationToken);
        return NoContent();
    }

    /// <summary>Begin authenticator-app enrolment; returns the shared secret (FR-EMP-003).</summary>
    [HttpPost("mfa/setup")]
    [Authorize]
    [ProducesResponseType(typeof(MfaSetupResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<MfaSetupResponse>> SetupMfa(CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        return Ok(await _auth.SetupMfaAsync(userId, cancellationToken));
    }

    /// <summary>Confirm enrolment with a valid code (FR-EMP-003).</summary>
    [HttpPost("mfa/enable")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> EnableMfa([FromBody] MfaEnableRequest request, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        await _auth.EnableMfaAsync(userId, request.Code, cancellationToken);
        return NoContent();
    }

    /// <summary>Turn off MFA after verifying a current code (FR-EMP-003).</summary>
    [HttpPost("mfa/disable")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DisableMfa([FromBody] MfaEnableRequest request, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        await _auth.DisableMfaAsync(userId, request.Code, cancellationToken);
        return NoContent();
    }
}
