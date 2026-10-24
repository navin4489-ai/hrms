using Wintech.Hrms.Api.Common.ErrorHandling;

namespace Wintech.Hrms.Api.Security.Auth;

/// <summary>
/// Rejects authenticated requests as soon as the account is suspended, terminated or past its
/// access end date (FR-EMP-006, BR-008) — even while a bearer token is still within its TTL.
/// </summary>
public sealed class AccountStatusMiddleware
{
    private readonly RequestDelegate _next;

    public AccountStatusMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var currentUser = context.RequestServices.GetRequiredService<Common.CurrentUser.ICurrentUser>();
            currentUser.EnsureActive();
        }

        await _next(context);
    }
}
