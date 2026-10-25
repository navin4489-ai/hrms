using System.Data;
using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Data;

namespace Wintech.Hrms.Api.Common.Tenancy;

/// <summary>
/// Additive upgrade for databases created before organization ownership was introduced.
/// This deliberately backfills the legacy single-organization dataset instead of dropping
/// existing HR data or relying on EnsureCreated to alter an existing schema.
/// </summary>
public static class TenantSchemaCompatibility
{
    private static readonly string[] ReferenceTables = ["OrgUnits", "Locations", "Designations"];

    public static async Task EnsureAsync(AppDbContext db, ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var provider = db.Database.ProviderName;
        if (string.Equals(provider, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal))
            await EnsureSqliteAsync(db, logger, cancellationToken);
        else if (string.Equals(provider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            await EnsurePostgresAsync(db, logger, cancellationToken);
        else if (string.Equals(provider, "Microsoft.EntityFrameworkCore.SqlServer", StringComparison.Ordinal))
            await EnsureSqlServerAsync(db, logger, cancellationToken);
    }

    private static async Task EnsureSqliteAsync(AppDbContext db, ILogger logger,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            foreach (var table in ReferenceTables)
            {
                if (!await SqliteColumnExistsAsync(connection, table, "OrganizationId", cancellationToken))
                {
                    await ExecuteAsync(connection,
                        $"ALTER TABLE \"{table}\" ADD COLUMN \"OrganizationId\" TEXT NULL",
                        cancellationToken);
                    logger.LogInformation("Added tenant ownership column to {Table}.", table);
                }

                await ExecuteAsync(connection,
                    $"UPDATE \"{table}\" SET \"OrganizationId\" = " +
                    "(SELECT \"Id\" FROM \"Organizations\" ORDER BY \"CreatedAt\" LIMIT 1) " +
                    "WHERE \"OrganizationId\" IS NULL OR \"OrganizationId\" = '00000000-0000-0000-0000-000000000000'",
                    cancellationToken);
            }

            await ExecuteAsync(connection, "DROP INDEX IF EXISTS \"IX_OrgUnits_Code\"", cancellationToken);
            await ExecuteAsync(connection, "DROP INDEX IF EXISTS \"IX_Locations_Code\"", cancellationToken);
            await ExecuteAsync(connection, "DROP INDEX IF EXISTS \"IX_Designations_Code\"", cancellationToken);
            await ExecuteAsync(connection,
                "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_OrgUnits_OrganizationId_Code\" ON \"OrgUnits\" (\"OrganizationId\", \"Code\")",
                cancellationToken);
            await ExecuteAsync(connection,
                "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Locations_OrganizationId_Code\" ON \"Locations\" (\"OrganizationId\", \"Code\")",
                cancellationToken);
            await ExecuteAsync(connection,
                "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Designations_OrganizationId_Code\" ON \"Designations\" (\"OrganizationId\", \"Code\")",
                cancellationToken);

            await ExecuteAsync(connection, """
                CREATE TABLE IF NOT EXISTS "OrgModuleSettings" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_OrgModuleSettings" PRIMARY KEY,
                    "OrganizationId" TEXT NOT NULL,
                    "ModuleKey" TEXT NOT NULL,
                    "Enabled" INTEGER NOT NULL,
                    "UpdatedAt" TEXT NOT NULL,
                    "UpdatedByUserId" TEXT NULL
                )
                """, cancellationToken);
            await ExecuteAsync(connection,
                "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_OrgModuleSettings_OrganizationId_ModuleKey\" ON \"OrgModuleSettings\" (\"OrganizationId\", \"ModuleKey\")",
                cancellationToken);
            await ExecuteAsync(connection, """
                CREATE TABLE IF NOT EXISTS "OrgEmployeeCodeSettings" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_OrgEmployeeCodeSettings" PRIMARY KEY,
                    "OrganizationId" TEXT NOT NULL,
                    "Prefix" TEXT NOT NULL,
                    "Padding" INTEGER NOT NULL,
                    "NextSequence" INTEGER NOT NULL
                )
                """, cancellationToken);
            await ExecuteAsync(connection,
                "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_OrgEmployeeCodeSettings_OrganizationId\" ON \"OrgEmployeeCodeSettings\" (\"OrganizationId\")",
                cancellationToken);
        }
        finally
        {
            if (openedHere) await db.Database.CloseConnectionAsync();
        }
    }

#pragma warning disable EF1002 // Identifiers are selected only from the private constant ReferenceTables list.
    private static async Task EnsurePostgresAsync(AppDbContext db, ILogger logger,
        CancellationToken cancellationToken)
    {
        const string schema = "wintech_hrms";
        foreach (var table in ReferenceTables)
        {
            await db.Database.ExecuteSqlRawAsync(
                $"ALTER TABLE IF EXISTS \"{schema}\".\"{table}\" ADD COLUMN IF NOT EXISTS \"OrganizationId\" uuid NULL",
                cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                $"UPDATE \"{schema}\".\"{table}\" SET \"OrganizationId\" = " +
                $"(SELECT \"Id\" FROM \"{schema}\".\"Organizations\" ORDER BY \"CreatedAt\" LIMIT 1) " +
                "WHERE \"OrganizationId\" IS NULL OR \"OrganizationId\" = '00000000-0000-0000-0000-000000000000'",
                cancellationToken);
        }

        await db.Database.ExecuteSqlRawAsync($"DROP INDEX IF EXISTS \"{schema}\".\"IX_OrgUnits_Code\"", cancellationToken);
        await db.Database.ExecuteSqlRawAsync($"DROP INDEX IF EXISTS \"{schema}\".\"IX_Locations_Code\"", cancellationToken);
        await db.Database.ExecuteSqlRawAsync($"DROP INDEX IF EXISTS \"{schema}\".\"IX_Designations_Code\"", cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            $"CREATE UNIQUE INDEX IF NOT EXISTS \"IX_OrgUnits_OrganizationId_Code\" ON \"{schema}\".\"OrgUnits\" (\"OrganizationId\", \"Code\")",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            $"CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Locations_OrganizationId_Code\" ON \"{schema}\".\"Locations\" (\"OrganizationId\", \"Code\")",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            $"CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Designations_OrganizationId_Code\" ON \"{schema}\".\"Designations\" (\"OrganizationId\", \"Code\")",
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync($"""
            CREATE TABLE IF NOT EXISTS "{schema}"."OrgModuleSettings" (
                "Id" uuid PRIMARY KEY,
                "OrganizationId" uuid NOT NULL,
                "ModuleKey" character varying(50) NOT NULL,
                "Enabled" boolean NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                "UpdatedByUserId" uuid NULL
            )
            """, cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            $"CREATE UNIQUE INDEX IF NOT EXISTS \"IX_OrgModuleSettings_OrganizationId_ModuleKey\" ON \"{schema}\".\"OrgModuleSettings\" (\"OrganizationId\", \"ModuleKey\")",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync($"""
            CREATE TABLE IF NOT EXISTS "{schema}"."OrgEmployeeCodeSettings" (
                "Id" uuid PRIMARY KEY,
                "OrganizationId" uuid NOT NULL,
                "Prefix" character varying(50) NOT NULL,
                "Padding" integer NOT NULL,
                "NextSequence" integer NOT NULL
            )
            """, cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            $"CREATE UNIQUE INDEX IF NOT EXISTS \"IX_OrgEmployeeCodeSettings_OrganizationId\" ON \"{schema}\".\"OrgEmployeeCodeSettings\" (\"OrganizationId\")",
            cancellationToken);
        logger.LogInformation("Verified tenant ownership schema for PostgreSQL.");
    }
#pragma warning restore EF1002

#pragma warning disable EF1002 // Identifiers are selected only from the private constant ReferenceTables list.
    private static async Task EnsureSqlServerAsync(AppDbContext db, ILogger logger,
        CancellationToken cancellationToken)
    {
        foreach (var table in ReferenceTables)
        {
            await db.Database.ExecuteSqlRawAsync($"""
                IF COL_LENGTH(N'dbo.{table}', N'OrganizationId') IS NULL
                    ALTER TABLE [dbo].[{table}] ADD [OrganizationId] uniqueidentifier NULL;
                UPDATE [dbo].[{table}]
                SET [OrganizationId] = (SELECT TOP (1) [Id] FROM [dbo].[Organizations] ORDER BY [CreatedAt])
                WHERE [OrganizationId] IS NULL;
                """, cancellationToken);
        }

        foreach (var table in ReferenceTables)
        {
            var oldIndex = $"IX_{table}_Code";
            var newIndex = $"IX_{table}_OrganizationId_Code";
            await db.Database.ExecuteSqlRawAsync($"""
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'{oldIndex}' AND object_id = OBJECT_ID(N'dbo.{table}'))
                    DROP INDEX [{oldIndex}] ON [dbo].[{table}];
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'{newIndex}' AND object_id = OBJECT_ID(N'dbo.{table}'))
                    CREATE UNIQUE INDEX [{newIndex}] ON [dbo].[{table}] ([OrganizationId], [Code]);
                """, cancellationToken);
        }

        await db.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID(N'dbo.OrgModuleSettings', N'U') IS NULL
                CREATE TABLE [dbo].[OrgModuleSettings] (
                    [Id] uniqueidentifier NOT NULL PRIMARY KEY,
                    [OrganizationId] uniqueidentifier NOT NULL,
                    [ModuleKey] nvarchar(50) NOT NULL,
                    [Enabled] bit NOT NULL,
                    [UpdatedAt] datetime2 NOT NULL,
                    [UpdatedByUserId] uniqueidentifier NULL
                );
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OrgModuleSettings_OrganizationId_ModuleKey')
                CREATE UNIQUE INDEX [IX_OrgModuleSettings_OrganizationId_ModuleKey] ON [dbo].[OrgModuleSettings] ([OrganizationId], [ModuleKey]);
            IF OBJECT_ID(N'dbo.OrgEmployeeCodeSettings', N'U') IS NULL
                CREATE TABLE [dbo].[OrgEmployeeCodeSettings] (
                    [Id] uniqueidentifier NOT NULL PRIMARY KEY,
                    [OrganizationId] uniqueidentifier NOT NULL,
                    [Prefix] nvarchar(50) NOT NULL,
                    [Padding] int NOT NULL,
                    [NextSequence] int NOT NULL
                );
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OrgEmployeeCodeSettings_OrganizationId')
                CREATE UNIQUE INDEX [IX_OrgEmployeeCodeSettings_OrganizationId] ON [dbo].[OrgEmployeeCodeSettings] ([OrganizationId]);
            """, cancellationToken);
        logger.LogInformation("Verified tenant ownership schema for SQL Server.");
    }
#pragma warning restore EF1002

    private static async Task<bool> SqliteColumnExistsAsync(System.Data.Common.DbConnection connection,
        string table, string column, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\")";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static async Task ExecuteAsync(System.Data.Common.DbConnection connection,
        string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
