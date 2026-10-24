namespace Wintech.Hrms.Api.Common.Configuration;

/// <summary>Typed configuration bound from appsettings.json sections.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public string Issuer { get; set; } = "wintech-hrms";
    public string Audience { get; set; } = "wintech-hrms-clients";
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
    public int MfaTokenMinutes { get; set; } = 5;
}

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";
    public string Provider { get; set; } = "SqlServer";
    public string? ConnectionString { get; set; }
    public string SqliteConnectionString { get; set; } = "Data Source=App_Data/wintech-hrms.dev.db";
    public bool Seed { get; set; } = true;
    public bool ExitOnInitializationFailure { get; set; } = true;

    public bool IsSqlServer => string.Equals(Provider, "SqlServer", StringComparison.OrdinalIgnoreCase);
    public bool IsSqlite => string.Equals(Provider, "Sqlite", StringComparison.OrdinalIgnoreCase);
    public bool IsInMemory => string.Equals(Provider, "InMemory", StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns the SQLite connection string with a fully qualified data source path.</summary>
    public string ResolveSqliteConnectionString(string contentRootPath)
    {
        var builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(SqliteConnectionString);
        if (!string.IsNullOrWhiteSpace(builder.DataSource) &&
            !string.Equals(builder.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase) &&
            !Path.IsPathRooted(builder.DataSource))
        {
            var full = Path.GetFullPath(Path.Combine(contentRootPath, builder.DataSource));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            builder.DataSource = full;
        }
        return builder.ToString();
    }
}

public sealed class SecurityOptions
{
    public const string SectionName = "Security";
    public bool RequireMfaForAdmins { get; set; }
    public long MaxUploadBytes { get; set; } = 10 * 1024 * 1024;
    public long AvatarMaxUploadBytes { get; set; } = 2 * 1024 * 1024;
    public string[] AllowedFileExtensions { get; set; } = [".pdf", ".png", ".jpg", ".jpeg", ".docx", ".xlsx", ".txt"];
    public bool AutoClearUploads { get; set; } = true;
    public bool GeofenceEnabled { get; set; }
    public int PasswordMinLength { get; set; } = 8;
    public int AttendanceCorrectionWindowDays { get; set; } = 30;
    public int LeaveCancellationWindowDays { get; set; } = 1;
    public int CaseReopenWindowDays { get; set; } = 3;
    public decimal ExpensePerClaimLimit { get; set; } = 100000;
}

public sealed class FileStorageOptions
{
    public const string SectionName = "FileStorage";
    public string Root { get; set; } = "App_Data/Files";
}

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";
    public int WindowSeconds { get; set; } = 60;
    public int PermitLimit { get; set; } = 200;
    public int AuthWindowSeconds { get; set; } = 60;
    public int AuthPermitLimit { get; set; } = 10;
}

public sealed class CorsOptions
{
    public const string SectionName = "Cors";
    public string[] AllowedOrigins { get; set; } = [];
}
