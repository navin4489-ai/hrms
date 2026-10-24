using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Wintech.Hrms.Api.Common.CurrentUser;
using Wintech.Hrms.Api.Common.ErrorHandling;

namespace Wintech.Hrms.Api.Security.Auth;

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

/// <summary>
/// Grants access when the caller's database-backed permissions contain the required code.
/// Usage: <c>[Permission(Permissions.LeaveApprove)]</c> on a controller or action.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class PermissionAttribute : Attribute, IAuthorizeData
{
    public const string Prefix = "Permission:";

    public PermissionAttribute(string permission)
    {
        Permission = permission;
        Policy = Prefix + permission;
    }

    public string Permission { get; }

    public string? Policy { get; set; }
    public string? Roles { get; set; }
    public string? AuthenticationSchemes { get; set; }
}

/// <summary>Builds a policy for every "Permission:code" name without pre-registering each code.</summary>
public sealed class PermissionPolicyProvider : DefaultAuthorizationPolicyProvider
{
    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : base(options) { }

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        var existing = await base.GetPolicyAsync(policyName);
        if (existing is not null) return existing;

        if (policyName.StartsWith(PermissionAttribute.Prefix, StringComparison.Ordinal))
        {
            var code = policyName[PermissionAttribute.Prefix.Length..];
            return new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(code))
                .Build();
        }

        return null;
    }
}

/// <summary>
/// Resolves the caller per request (never trusts client-supplied roles) and succeeds only
/// when the effective permission set contains the required code (BRD section 4.2).
/// </summary>
public sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PermissionHandler(IHttpContextAccessor httpContextAccessor) => _httpContextAccessor = httpContextAccessor;

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null) return Task.CompletedTask;

        var currentUser = httpContext.RequestServices.GetService<ICurrentUser>();
        if (currentUser is null || !currentUser.IsAuthenticated) return Task.CompletedTask;

        currentUser.EnsureActive();
        if (currentUser.HasPermission(requirement.Permission)) context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
