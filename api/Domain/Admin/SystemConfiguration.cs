using Wintech.Hrms.Api.Domain.Identity;

namespace Wintech.Hrms.Api.Domain.Admin;

/// <summary>
/// Tenant-wide configuration key/value exposed by GET/PUT /api/v1/admin/configuration
/// (FR-ADM-014, BR-012: changes apply prospectively — the previous value is kept in the audit trail).
/// The <see cref="Type"/> column restricts <see cref="Value"/> to one of "String|Int|Bool|Date|Json".
/// </summary>
public class SystemConfiguration
{
    public Guid Id { get; set; }

    /// <summary>Dotted configuration key, e.g. <c>Attendance.GraceMinutes</c>. Unique.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Current value as text; interpreted according to <see cref="Type"/>.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>One of "String", "Int", "Bool", "Date", "Json" (contract §17).</summary>
    public string Type { get; set; } = "String";

    public string Description { get; set; } = string.Empty;

    /// <summary>Null until an administrator changes the value through the API.</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>User who last changed the value through the API (null for seeded values).</summary>
    public Guid? UpdatedByUserId { get; set; }

    public UserAccount? UpdatedByUser { get; set; }
}
