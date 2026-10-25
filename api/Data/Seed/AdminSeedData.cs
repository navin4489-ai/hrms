using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.Registration;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Admin;
using Wintech.Hrms.Api.Domain.Identity;
using Wintech.Hrms.Api.Security;

namespace Wintech.Hrms.Api.Data.Seed;

/// <summary>
/// Idempotent admin foundation seed (runs after the core seeder, Order 150): the tenant-wide
/// configuration catalogue exposed by GET/api/v1/admin/configuration, plus one non-system demo role
/// so role/permission management (PUT /admin/roles/{name}/permissions) has a safe target that is
/// never re-synchronised by <see cref="CoreSeeder"/>. Existing rows are never touched.
/// </summary>
public sealed class AdminSeedData : IDataSeeder
{
    /// <summary>Custom (IsSystem = false) role used to demonstrate admin-managed permissions.</summary>
    public const string CustomRoleName = "OpsCoordinator";

    private readonly AppDbContext _db;
    private readonly ILogger<AdminSeedData> _logger;

    public AdminSeedData(AppDbContext db, ILogger<AdminSeedData> logger)
    {
        _db = db;
        _logger = logger;
    }

    public int Order => 150;

    private static readonly (string Key, string Type, string Value, string Description)[] ConfigurationSeeds =
    [
        ("Attendance.GraceMinutes", "Int", "5",
            "Minutes after shift start before a punch is counted as late."),
        ("Leave.MaxCarryForwardDays", "Int", "12",
            "Maximum unused leave days carried into the next leave year."),
        ("Security.PasswordExpiryDays", "Int", "90",
            "Days before a password must be rotated (0 disables expiry)."),
        ("Cases.SlaHours", "Int", "24",
            "Hours allowed for first response on an HR helpdesk case."),
        ("Files.MaxUploadMb", "Int", "10",
            "Maximum single file upload size in megabytes."),
        ("Security.RequireMfaForAdmins", "Bool", "false",
            "Require MFA enrolment for privileged roles (FR-ADM-143)."),
        ("Leave.YearStart", "Date", "2026-01-01",
            "First day of the leave year used for accruals."),
        ("Attendance.CaptureLocation", "Bool", "true",
            "Capture geolocation on employee check-in/out (admin-only visibility, FR-ADM-041)."),
        ("Attendance.CapturePhoto", "Bool", "true",
            "Capture a selfie on employee check-in/out (admin-only visibility, FR-ADM-041).")
    ];

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var added = 0;

        foreach (var (key, type, value, description) in ConfigurationSeeds)
        {
            var exists = await _db.Set<SystemConfiguration>()
                .AnyAsync(c => c.Key == key, cancellationToken);
            if (exists) continue;

            _db.Set<SystemConfiguration>().Add(new SystemConfiguration
            {
                Id = Guid.NewGuid(),
                Key = key,
                Value = value,
                Type = type,
                Description = description
            });
            added++;
        }

        var roleExists = await _db.Roles.AnyAsync(r => r.Name == CustomRoleName, cancellationToken);
        if (!roleExists)
        {
            var role = new Role
            {
                Id = Guid.NewGuid(),
                Name = CustomRoleName,
                Description = "Custom (non-system) operational role managed through the admin console.",
                IsSystem = false
            };
            role.RolePermissions.Add(new RolePermission
            {
                RoleId = role.Id,
                PermissionCode = Permissions.ReportView
            });
            _db.Roles.Add(role);
            added++;
        }

        if (added == 0)
        {
            _logger.LogDebug("Admin configuration/roles already seeded; nothing to do.");
            return;
        }

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Admin seed added {Count} configuration key(s)/role(s).", added);
    }
}
