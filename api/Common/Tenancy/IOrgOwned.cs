namespace Wintech.Hrms.Api.Common.Tenancy;

/// <summary>
/// Marks a domain entity as owned by a single organization (tenant). Every entity implementing
/// this interface automatically gets:
/// <list type="bullet">
///   <item>an indexed <c>OrganizationId</c> column,</item>
///   <item>a global query filter restricting reads to the caller's organization,</item>
///   <item>automatic stamping of <c>OrganizationId</c> on insert from the ambient tenant scope.</item>
/// </list>
/// SuperAdmins (and seeding/background work) bypass the filter via <see cref="ITenantContext.BypassFilter"/>.
/// </summary>
public interface IOrgOwned
{
    Guid OrganizationId { get; set; }
}
