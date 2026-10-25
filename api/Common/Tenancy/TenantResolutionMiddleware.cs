using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.ErrorHandling;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Security;

namespace Wintech.Hrms.Api.Common.Tenancy;

/// <summary>
/// Resolves the request's organization scope immediately after authentication so the global
/// tenant query filter can isolate data per organization.
/// <para>
/// SuperAdmins bypass the filter (cross-org). They may target a specific organization by sending
/// an <c>X-Organization-Id</c> header; writes then land in that organization.
/// </para>
/// </summary>
public sealed class TenantResolutionMiddleware
{
    public const string OrganizationHeader = "X-Organization-Id";

    private readonly RequestDelegate _next;

    public TenantResolutionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ITenantContext tenant, AppDbContext db)
    {
        var scope = (TenantContext)tenant;

        var userIdClaim = context.User.FindFirst("sub")?.Value
                          ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (context.User.Identity?.IsAuthenticated == true && Guid.TryParse(userIdClaim, out var userId))
        {
            // Resolve identity while bypassing the filter (the user's own row would otherwise be hidden).
            scope.Set(null, bypassFilter: true);

            var profile = await db.UserAccounts.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new
                {
                    OrganizationId = u.Employee != null ? (Guid?)u.Employee.OrganizationId : null,
                     IsSuperAdmin = u.UserRoles.Any(r => r.Role != null && r.Role.Name == Roles.SuperAdmin)
                })
                .FirstOrDefaultAsync(context.RequestAborted);

            if (profile is null)
            {
                scope.Set(null, bypassFilter: false);
            }
            else if (profile.IsSuperAdmin)
            {
                // SuperAdmin: cross-org by default; optionally pinned to a target organization.
                Guid? target = profile.OrganizationId;
                var bypassFilter = true;
                if (context.Request.Headers.TryGetValue(OrganizationHeader, out var header))
                {
                    if (!Guid.TryParse(header.ToString(), out var requested))
                        throw new RequestValidationException("The organization header is invalid.",
                            new() { [OrganizationHeader] = ["Provide a valid organization id."] });
                    if (!await db.Organizations.AsNoTracking()
                            .AnyAsync(org => org.Id == requested, context.RequestAborted))
                        throw new NotFoundException("The requested organization was not found.");
                    target = requested;
                    bypassFilter = false;
                }
                scope.Set(target, bypassFilter);
            }
            else
            {
                scope.Set(profile.OrganizationId, bypassFilter: false);
            }
        }
        else
        {
            // Anonymous: no org scope, filter active (matches nothing for org-owned entities).
            scope.Set(null, bypassFilter: false);
        }

        await _next(context);
    }
}
