namespace Wintech.Hrms.Api.Common.Tenancy;

/// <summary>
/// Ambient, request-scoped organization scope used to enforce row-level tenant isolation.
/// Populated once per request from the authenticated user's organization.
/// <para>
/// When <see cref="BypassFilter"/> is true (SuperAdmin, seeding, background work) the global
/// query filter is disabled and the caller may read/write across organizations.
/// </para>
/// </summary>
public interface ITenantContext
{
    /// <summary>The caller's organization id, or null when unknown/anonymous.</summary>
    Guid? OrganizationId { get; }

    /// <summary>When true, the tenant query filter is not applied (cross-org access).</summary>
    bool BypassFilter { get; }

    /// <summary>True once the request pipeline has resolved a scope (vs. seed/background work).</summary>
    bool IsScopeSet { get; }

    /// <summary>Resolve the organization a new row must belong to, or throw when ambiguous.</summary>
    Guid RequireOrganizationId();

    /// <summary>Overrides the ambient scope for the lifetime of the returned handle.</summary>
    IDisposable Use(Guid? organizationId, bool bypassFilter);
}

public sealed class TenantContext : ITenantContext
{
    private Guid? _organizationId;
    private bool _bypass;
    private bool _explicitlySet;

    public Guid? OrganizationId => _organizationId;
    public bool BypassFilter => _bypass;
    public bool IsScopeSet => _explicitlySet;

    internal bool IsSet => _explicitlySet;

    public void Set(Guid? organizationId, bool bypassFilter)
    {
        _organizationId = organizationId;
        _bypass = bypassFilter;
        _explicitlySet = true;
    }

    public Guid RequireOrganizationId()
    {
        if (_organizationId is { } id && id != Guid.Empty) return id;
        throw new InvalidOperationException(
            "No organization is in scope for this operation. SuperAdmins must specify a target organization.");
    }

    public IDisposable Use(Guid? organizationId, bool bypassFilter)
    {
        var previousOrg = _organizationId;
        var previousBypass = _bypass;
        var previousSet = _explicitlySet;
        _organizationId = organizationId;
        _bypass = bypassFilter;
        _explicitlySet = true;
        return new Scope(() =>
        {
            _organizationId = previousOrg;
            _bypass = previousBypass;
            _explicitlySet = previousSet;
        });
    }

    private sealed class Scope(Action onDispose) : IDisposable
    {
        private Action? _onDispose = onDispose;
        public void Dispose()
        {
            _onDispose?.Invoke();
            _onDispose = null;
        }
    }
}
