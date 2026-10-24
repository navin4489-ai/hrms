using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Wintech.Hrms.Api.Domain.Employees;
using Wintech.Hrms.Api.Security;

namespace Wintech.Hrms.Api.Data;

/// <summary>
/// Optional, one-time production bootstrap for changing the seeded super-admin login.
/// Credentials are supplied only through deployment environment variables and are never logged.
/// </summary>
public static class AdminAccountBootstrapper
{
    private const string Schema = "wintech_hrms";
    private const string MarkerTable = "__admin_bootstrap";
    private const string ProfileBootstrapMarkerId = "2";

    public static async Task RunIfEnabledAsync(
        AppDbContext db,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(db.Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal) ||
            !configuration.GetValue<bool>("Security:AdminBootstrapEnabled"))
            return;

        var email = configuration["Security:AdminBootstrapEmail"]?.Trim().ToLowerInvariant();
        var password = configuration["Security:AdminBootstrapPassword"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Admin bootstrap is enabled but the email or password secret is missing.");

        if (!MailAddress.TryCreate(email, out var parsedEmail) ||
            !string.Equals(parsedEmail.Address, email, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Admin bootstrap email is not a valid email address.");

        if (password.Length < 12 || !password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            throw new InvalidOperationException("Admin bootstrap password must be at least 12 characters and include letters and numbers.");

        await db.Database.ExecuteSqlRawAsync(
            $"CREATE TABLE IF NOT EXISTS \"{Schema}\".\"{MarkerTable}\" (\"id\" integer PRIMARY KEY)",
            cancellationToken);

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var connection = db.Database.GetDbConnection();
            await using (var markerCheck = connection.CreateCommand())
            {
                markerCheck.Transaction = transaction.GetDbTransaction();
                markerCheck.CommandText = $"SELECT EXISTS (SELECT 1 FROM \"{Schema}\".\"{MarkerTable}\" WHERE \"id\" = 1)";
                if ((bool)(await markerCheck.ExecuteScalarAsync(cancellationToken) ?? false))
                {
                    await transaction.CommitAsync(cancellationToken);
                    logger.LogInformation("One-time admin bootstrap was already completed; no account changes made.");
                    return;
                }
            }

            var admin = await db.UserAccounts
                .Include(user => user.UserRoles)
                .ThenInclude(userRole => userRole.Role)
                .SingleOrDefaultAsync(user => user.UserName == "admin", cancellationToken)
                ?? throw new InvalidOperationException("The seeded admin account was not found; bootstrap did not run.");

            if (!admin.UserRoles.Any(userRole => userRole.Role?.Name == "SuperAdmin"))
                throw new InvalidOperationException("The seeded admin account does not have the SuperAdmin role.");

            var identifierInUse = await db.UserAccounts.AsNoTracking().AnyAsync(user =>
                user.Id != admin.Id &&
                (user.UserName.ToLower() == email || (user.Email != null && user.Email.ToLower() == email)),
                cancellationToken);
            if (identifierInUse)
                throw new InvalidOperationException("The requested admin email is already assigned to another account.");

            var now = DateTime.UtcNow;
            admin.UserName = email;
            admin.Email = email;
            admin.PasswordHash = PasswordHasher.Hash(password);
            admin.FailedLoginCount = 0;
            admin.MustChangePassword = false;
            admin.UpdatedAt = now;

            var activeSessions = await db.RefreshTokens
                .Where(token => token.UserId == admin.Id && token.RevokedAt == null)
                .ToListAsync(cancellationToken);
            foreach (var session in activeSessions)
                session.RevokedAt = now;

            await db.SaveChangesAsync(cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                $"INSERT INTO \"{Schema}\".\"{MarkerTable}\" (\"id\") VALUES (1)",
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation("One-time admin bootstrap completed for {AdminEmail}; active sessions were revoked.", email);
        });
    }

    /// <summary>
    /// One-time test admin profile separation. The seeded Ravi/EMP001 record is kept intact;
    /// the existing SuperAdmin account is linked to a new employee row using the same org placement.
    /// </summary>
    public static async Task RunProfileIfEnabledAsync(
        AppDbContext db,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(db.Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal) ||
            !configuration.GetValue<bool>("Security:AdminProfileBootstrapEnabled"))
            return;

        var email = configuration["Security:AdminProfileBootstrapEmail"]?.Trim().ToLowerInvariant();
        var firstName = configuration["Security:AdminProfileBootstrapFirstName"]?.Trim();
        var username = configuration["Security:AdminProfileBootstrapUserName"]?.Trim().ToLowerInvariant();
        var password = configuration["Security:AdminProfileBootstrapPassword"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(firstName) ||
            string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Admin profile bootstrap is enabled but a required value is missing.");

        if (!MailAddress.TryCreate(email, out var parsedEmail) ||
            !string.Equals(parsedEmail.Address, email, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Admin profile bootstrap email is not valid.");

        if (password.Length < 12 || !password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            throw new InvalidOperationException("Admin profile bootstrap password must be at least 12 characters and include letters and numbers.");

        await db.Database.ExecuteSqlRawAsync(
            $"CREATE TABLE IF NOT EXISTS \"{Schema}\".\"{MarkerTable}\" (\"id\" integer PRIMARY KEY)",
            cancellationToken);

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var connection = db.Database.GetDbConnection();
            await using (var markerCheck = connection.CreateCommand())
            {
                markerCheck.Transaction = transaction.GetDbTransaction();
                markerCheck.CommandText = $"SELECT EXISTS (SELECT 1 FROM \"{Schema}\".\"{MarkerTable}\" WHERE \"id\" = {ProfileBootstrapMarkerId})";
                if ((bool)(await markerCheck.ExecuteScalarAsync(cancellationToken) ?? false))
                {
                    await transaction.CommitAsync(cancellationToken);
                    logger.LogInformation("One-time admin profile bootstrap was already completed; no account changes made.");
                    return;
                }
            }

            var admin = await db.UserAccounts
                .Include(user => user.UserRoles)
                .ThenInclude(userRole => userRole.Role)
                .Include(user => user.Employee)
                .SingleOrDefaultAsync(user => user.Email != null && user.Email.ToLower() == email, cancellationToken)
                ?? throw new InvalidOperationException("The admin login account was not found; profile bootstrap did not run.");

            if (!admin.UserRoles.Any(userRole => userRole.Role?.Name == "SuperAdmin"))
                throw new InvalidOperationException("The account does not have the SuperAdmin role.");

            var template = admin.Employee
                ?? throw new InvalidOperationException("The admin account has no employee profile to use as an org template.");

            var duplicateEmail = await db.Employees.AsNoTracking().AnyAsync(
                employee => employee.WorkEmail.ToLower() == email && employee.Id != template.Id,
                cancellationToken);
            if (duplicateEmail)
                throw new InvalidOperationException("The requested admin email is already assigned to another employee.");

            var usernameTaken = await db.UserAccounts.AsNoTracking().AnyAsync(
                user => user.Id != admin.Id && user.UserName.ToLower() == username,
                cancellationToken);
            if (usernameTaken)
                throw new InvalidOperationException("The requested admin username is already in use.");

            var numbers = await db.Employees.AsNoTracking()
                .Select(employee => employee.EmployeeNumber)
                .ToListAsync(cancellationToken);
            var maxNumber = numbers
                .Where(number => number.StartsWith("EMP", StringComparison.OrdinalIgnoreCase))
                .Select(number => int.TryParse(number.AsSpan(3), out var value) ? value : 0)
                .DefaultIfEmpty(0)
                .Max();

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var now = DateTime.UtcNow;
            var employeeProfile = new Employee
            {
                Id = Guid.NewGuid(),
                EmployeeNumber = $"EMP{maxNumber + 1:D3}",
                OrganizationId = template.OrganizationId,
                FirstName = firstName,
                LastName = string.Empty,
                WorkEmail = email,
                JoinDate = today,
                EmploymentType = template.EmploymentType,
                EmploymentStatus = EmploymentStatus.Active,
                StatusEffectiveDate = today,
                DesignationId = template.DesignationId,
                DepartmentId = template.DepartmentId,
                LocationId = template.LocationId,
                ManagerId = null
            };

            db.Employees.Add(employeeProfile);
            db.EmploymentHistories.Add(new EmploymentHistory
            {
                Id = Guid.NewGuid(),
                EmployeeId = employeeProfile.Id,
                EventType = HistoryEventType.Joining,
                EffectiveDate = today,
                NewValue = $"Joined as {template.Designation?.Name ?? "Administrator"}",
                ApprovedBy = "Admin profile bootstrap",
                CreatedAt = now
            });

            admin.EmployeeId = employeeProfile.Id;
            admin.Employee = employeeProfile;
            admin.UserName = username;
            admin.PasswordHash = PasswordHasher.Hash(password);
            admin.MustChangePassword = true;
            admin.FailedLoginCount = 0;
            admin.UpdatedAt = now;

            var sessions = await db.RefreshTokens
                .Where(token => token.UserId == admin.Id && token.RevokedAt == null)
                .ToListAsync(cancellationToken);
            foreach (var session in sessions)
                session.RevokedAt = now;

            await db.SaveChangesAsync(cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                $"INSERT INTO \"{Schema}\".\"{MarkerTable}\" (\"id\") VALUES ({ProfileBootstrapMarkerId})",
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation(
                "One-time Naveen admin profile created as {EmployeeNumber}; the seeded employee record was preserved.",
                employeeProfile.EmployeeNumber);
        });
    }
}
