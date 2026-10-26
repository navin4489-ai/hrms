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

        await BackfillOrgDefaultsAsync(db, logger, cancellationToken);
        await EnsureChatTablesAsync(db, logger, cancellationToken);
        await AttendanceSchemaCompatibility.EnsureAsync(db, logger, cancellationToken);
    }

    /// <summary>
    /// Creates the chat/calling tables on databases that predate those features. The production DB
    /// was first created with EnsureCreated before the chat entities existed, and EnsureCreated does
    /// not add new tables to an existing database, so <c>/chat/*</c> queries fail with 500 until the
    /// tables exist. Idempotent: uses CREATE TABLE IF NOT EXISTS. Provider-aware.
    /// </summary>
    private static async Task EnsureChatTablesAsync(AppDbContext db, ILogger logger,
        CancellationToken cancellationToken)
    {
        var provider = db.Database.ProviderName;
        var isPg = string.Equals(provider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal);
        var isSqlite = string.Equals(provider, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal);
        var isSqlServer = string.Equals(provider, "Microsoft.EntityFrameworkCore.SqlServer", StringComparison.Ordinal);
        if (!isPg && !isSqlite && !isSqlServer) return;

        // Column type map per provider.
        string guid = isPg ? "uuid" : isSqlite ? "TEXT" : "uniqueidentifier";
        string text = isPg ? "text" : isSqlite ? "TEXT" : "nvarchar(max)";
        string vc(int n) => isPg ? $"character varying({n})" : isSqlite ? "TEXT" : $"nvarchar({n})";
        string boolean = isPg ? "boolean" : isSqlite ? "INTEGER" : "bit";
        string integer = isPg ? "integer" : isSqlite ? "INTEGER" : "int";
        string bigint = isPg ? "bigint" : isSqlite ? "INTEGER" : "bigint";
        string ts = isPg ? "timestamp with time zone" : isSqlite ? "TEXT" : "datetime2";
        string pfx = isPg ? "\"wintech_hrms\"." : string.Empty;

        async Task Exec(string sql)
        {
            try { await db.Database.ExecuteSqlRawAsync(sql, cancellationToken); }
            catch (Exception ex) { logger.LogWarning(ex, "Chat table DDL skipped/failed: {Sql}", sql.Split('\n')[0]); }
        }

        string ine = isSqlServer ? "" : "IF NOT EXISTS ";
        // SQL Server lacks CREATE TABLE IF NOT EXISTS; guard with OBJECT_ID.
        async Task CreateTable(string name, string body)
        {
            if (isSqlServer)
                await Exec($"IF OBJECT_ID(N'{name}', N'U') IS NULL CREATE TABLE [{name}] ({body});");
            else
                await Exec($"CREATE TABLE {ine}{pfx}\"{name}\" ({body})");
        }
        async Task CreateIndex(string idx, string table, string cols, bool unique = false)
        {
            var u = unique ? "UNIQUE " : "";
            if (isSqlServer)
                await Exec($"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'{idx}') CREATE {u}INDEX [{idx}] ON [{table}] ({cols});");
            else
                await Exec($"CREATE {u}INDEX IF NOT EXISTS \"{idx}\" ON {pfx}\"{table}\" ({cols})");
        }

        await CreateTable("chat_conversations",
            $"\"Id\" {guid} PRIMARY KEY, \"OrganizationId\" {guid} NOT NULL, \"Type\" {integer} NOT NULL, " +
            $"\"Name\" {vc(200)} NULL, \"DirectKey\" {vc(100)} NULL, \"CreatedByEmployeeId\" {guid} NOT NULL, " +
            $"\"CreatedAt\" {ts} NOT NULL, \"LastMessageAt\" {ts} NOT NULL, \"LastMessagePreview\" {vc(300)} NULL");
        await CreateIndex("IX_chat_conversations_LastMessageAt", "chat_conversations", "\"LastMessageAt\"");

        await CreateTable("chat_members",
            $"\"Id\" {guid} PRIMARY KEY, \"OrganizationId\" {guid} NOT NULL, \"ConversationId\" {guid} NOT NULL, " +
            $"\"EmployeeId\" {guid} NOT NULL, \"IsAdmin\" {boolean} NOT NULL, \"JoinedAt\" {ts} NOT NULL, " +
            $"\"LeftAt\" {ts} NULL, \"LastReadAt\" {ts} NULL");
        await CreateIndex("IX_chat_members_ConversationId_EmployeeId", "chat_members", "\"ConversationId\", \"EmployeeId\"", unique: true);
        await CreateIndex("IX_chat_members_EmployeeId", "chat_members", "\"EmployeeId\"");

        await CreateTable("chat_messages",
            $"\"Id\" {guid} PRIMARY KEY, \"OrganizationId\" {guid} NOT NULL, \"ConversationId\" {guid} NOT NULL, " +
            $"\"SenderEmployeeId\" {guid} NOT NULL, \"Kind\" {integer} NOT NULL, \"Body\" {vc(4000)} NULL, " +
            $"\"AttachmentStorageKey\" {vc(400)} NULL, \"AttachmentFileName\" {vc(260)} NULL, " +
            $"\"AttachmentContentType\" {vc(150)} NULL, \"AttachmentSize\" {bigint} NULL, " +
            $"\"DurationSeconds\" {integer} NULL, \"CreatedAt\" {ts} NOT NULL, \"Deleted\" {boolean} NOT NULL");
        await CreateIndex("IX_chat_messages_ConversationId_CreatedAt", "chat_messages", "\"ConversationId\", \"CreatedAt\"");

        await CreateTable("chat_calls",
            $"\"Id\" {guid} PRIMARY KEY, \"OrganizationId\" {guid} NOT NULL, \"CallerEmployeeId\" {guid} NOT NULL, " +
            $"\"CalleeEmployeeId\" {guid} NOT NULL, \"Kind\" {integer} NOT NULL, \"State\" {integer} NOT NULL, " +
            $"\"StartedAt\" {ts} NOT NULL, \"AnsweredAt\" {ts} NULL, \"EndedAt\" {ts} NULL, \"DurationSeconds\" {integer} NULL");
        await CreateIndex("IX_chat_calls_CalleeEmployeeId_State", "chat_calls", "\"CalleeEmployeeId\", \"State\"");
        await CreateIndex("IX_chat_calls_StartedAt", "chat_calls", "\"StartedAt\"");

        await CreateTable("chat_device_tokens",
            $"\"Id\" {guid} PRIMARY KEY, \"OrganizationId\" {guid} NOT NULL, \"EmployeeId\" {guid} NOT NULL, " +
            $"\"Token\" {vc(400)} NOT NULL, \"Platform\" {vc(20)} NULL, \"UpdatedAt\" {ts} NOT NULL");
        await CreateIndex("IX_chat_device_tokens_Token", "chat_device_tokens", "\"Token\"", unique: true);
        await CreateIndex("IX_chat_device_tokens_EmployeeId", "chat_device_tokens", "\"EmployeeId\"");

        // Fail startup if durable attachment storage cannot be provisioned.
        // Unlike the legacy DDL helper, do not swallow this error.
        var binary = isPg ? "bytea" : isSqlite ? "BLOB" : "varbinary(max)";
        var contentBody = $"\"Id\" {guid} PRIMARY KEY REFERENCES {pfx}\"chat_messages\" (\"Id\") ON DELETE CASCADE, " +
            $"\"OrganizationId\" {guid} NOT NULL, \"Content\" {binary} NOT NULL";
        var contentSql = isSqlServer
            ? $"IF OBJECT_ID(N'chat_attachment_content', N'U') IS NULL CREATE TABLE [chat_attachment_content] ({contentBody});"
            : $"CREATE TABLE IF NOT EXISTS {pfx}\"chat_attachment_content\" ({contentBody})";
        await db.Database.ExecuteSqlRawAsync(contentSql, cancellationToken);

        _ = text;
        logger.LogInformation("Verified chat/calling tables.");
    }

    /// <summary>
    /// Ensures every existing organization has per-org module flags and an employee-code setting.
    /// Organizations created before these features shipped would otherwise fall back to module
    /// defaults (Chat/Calling off), hiding those features in the app. Provider-agnostic (EF).
    /// </summary>
    private static async Task BackfillOrgDefaultsAsync(AppDbContext db, ILogger logger,
        CancellationToken cancellationToken)
    {
        var orgs = await db.Organizations.AsNoTracking()
            .Select(o => new { o.Id, o.Code })
            .ToListAsync(cancellationToken);
        if (orgs.Count == 0) return;

        // Load existing module rows (tracked) so we can fill gaps and force-enable collaboration.
        var allModules = await db.Set<Domain.Organization.OrgModuleSetting>()
            .IgnoreQueryFilters()
            .ToListAsync(cancellationToken);
        var modulesByOrg = allModules
            .GroupBy(m => m.OrganizationId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var existingCodeOrgIds = await db.Set<Domain.Organization.OrgEmployeeCodeSetting>()
            .IgnoreQueryFilters().AsNoTracking()
            .Select(s => s.OrganizationId).ToListAsync(cancellationToken);
        var existingCodeSet = existingCodeOrgIds.ToHashSet();

        var touched = 0;
        foreach (var org in orgs)
        {
            var rows = modulesByOrg.GetValueOrDefault(org.Id) ?? new List<Domain.Organization.OrgModuleSetting>();
            var byKey = rows.ToDictionary(m => m.ModuleKey, m => m, StringComparer.OrdinalIgnoreCase);

            foreach (var key in OrgModules.AllKeys)
            {
                // Chat + Calling are force-enabled on every existing tenant after upgrade;
                // other modules keep their value, defaulting to the module default when missing.
                var forceOn = key == OrgModules.Chat || key == OrgModules.Calling;

                if (byKey.TryGetValue(key, out var row))
                {
                    if (forceOn && !row.Enabled)
                    {
                        row.Enabled = true;
                        row.UpdatedAt = DateTime.UtcNow;
                        touched++;
                    }
                }
                else
                {
                    db.Add(new Domain.Organization.OrgModuleSetting
                    {
                        Id = Guid.NewGuid(),
                        OrganizationId = org.Id,
                        ModuleKey = key,
                        Enabled = forceOn || OrgModules.DefaultFor(key),
                        UpdatedAt = DateTime.UtcNow
                    });
                    touched++;
                }
            }

            if (!existingCodeSet.Contains(org.Id))
            {
                db.Add(new Domain.Organization.OrgEmployeeCodeSetting
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = org.Id,
                    Prefix = string.Equals(org.Code, "WINTECH", StringComparison.OrdinalIgnoreCase)
                        ? "EMP"
                        : $"{org.Code}-EMP",
                    Padding = 3,
                    NextSequence = 1
                });
                touched++;
            }
        }

        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Backfilled/updated per-org module/code settings ({Count} change(s)).", touched);
        }
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
