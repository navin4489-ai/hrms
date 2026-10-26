using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Data;

namespace Wintech.Hrms.Api.Common.Tenancy;

/// <summary>Additive upgrade for databases created before punch capture was introduced.</summary>
public static class AttendanceSchemaCompatibility
{
    public static async Task EnsureAsync(AppDbContext db, ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var provider = db.Database.ProviderName;
        var sql = provider switch
        {
            "Npgsql.EntityFrameworkCore.PostgreSQL" => """
                CREATE TABLE IF NOT EXISTS "wintech_hrms"."att_punch_events" (
                    "Id" uuid NOT NULL PRIMARY KEY,
                    "EmployeeId" uuid NOT NULL REFERENCES "wintech_hrms"."Employees" ("Id") ON DELETE RESTRICT,
                    "AttendanceRecordId" uuid NOT NULL,
                    "WorkDate" date NOT NULL,
                    "Direction" integer NOT NULL,
                    "CapturedAtUtc" timestamp with time zone NOT NULL,
                    "LocalTime" time without time zone NOT NULL,
                    "Latitude" double precision NULL,
                    "Longitude" double precision NULL,
                    "AccuracyMeters" double precision NULL,
                    "LocationName" character varying(300) NULL,
                    "PhotoStorageKey" character varying(300) NULL,
                    "PhotoContentType" character varying(100) NULL,
                    "Source" integer NOT NULL
                );
                CREATE INDEX IF NOT EXISTS "IX_att_punch_events_EmployeeId_WorkDate"
                    ON "wintech_hrms"."att_punch_events" ("EmployeeId", "WorkDate");
                CREATE INDEX IF NOT EXISTS "IX_att_punch_events_WorkDate"
                    ON "wintech_hrms"."att_punch_events" ("WorkDate");
                CREATE INDEX IF NOT EXISTS "IX_att_punch_events_CapturedAtUtc"
                    ON "wintech_hrms"."att_punch_events" ("CapturedAtUtc");
                """,
            "Microsoft.EntityFrameworkCore.Sqlite" => """
                CREATE TABLE IF NOT EXISTS "att_punch_events" (
                    "Id" TEXT NOT NULL PRIMARY KEY,
                    "EmployeeId" TEXT NOT NULL REFERENCES "Employees" ("Id") ON DELETE RESTRICT,
                    "AttendanceRecordId" TEXT NOT NULL,
                    "WorkDate" TEXT NOT NULL,
                    "Direction" INTEGER NOT NULL,
                    "CapturedAtUtc" TEXT NOT NULL,
                    "LocalTime" TEXT NOT NULL,
                    "Latitude" REAL NULL,
                    "Longitude" REAL NULL,
                    "AccuracyMeters" REAL NULL,
                    "LocationName" TEXT NULL,
                    "PhotoStorageKey" TEXT NULL,
                    "PhotoContentType" TEXT NULL,
                    "Source" INTEGER NOT NULL
                );
                CREATE INDEX IF NOT EXISTS "IX_att_punch_events_EmployeeId_WorkDate"
                    ON "att_punch_events" ("EmployeeId", "WorkDate");
                CREATE INDEX IF NOT EXISTS "IX_att_punch_events_WorkDate"
                    ON "att_punch_events" ("WorkDate");
                CREATE INDEX IF NOT EXISTS "IX_att_punch_events_CapturedAtUtc"
                    ON "att_punch_events" ("CapturedAtUtc");
                """,
            "Microsoft.EntityFrameworkCore.SqlServer" => """
                IF OBJECT_ID(N'dbo.att_punch_events', N'U') IS NULL
                    CREATE TABLE [dbo].[att_punch_events] (
                        [Id] uniqueidentifier NOT NULL PRIMARY KEY,
                        [EmployeeId] uniqueidentifier NOT NULL REFERENCES [dbo].[Employees] ([Id]),
                        [AttendanceRecordId] uniqueidentifier NOT NULL,
                        [WorkDate] date NOT NULL,
                        [Direction] int NOT NULL,
                        [CapturedAtUtc] datetime2 NOT NULL,
                        [LocalTime] time NOT NULL,
                        [Latitude] float NULL,
                        [Longitude] float NULL,
                        [AccuracyMeters] float NULL,
                        [LocationName] nvarchar(300) NULL,
                        [PhotoStorageKey] nvarchar(300) NULL,
                        [PhotoContentType] nvarchar(100) NULL,
                        [Source] int NOT NULL
                    );
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_att_punch_events_EmployeeId_WorkDate'
                    AND object_id = OBJECT_ID(N'dbo.att_punch_events'))
                    CREATE INDEX [IX_att_punch_events_EmployeeId_WorkDate]
                        ON [dbo].[att_punch_events] ([EmployeeId], [WorkDate]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_att_punch_events_WorkDate'
                    AND object_id = OBJECT_ID(N'dbo.att_punch_events'))
                    CREATE INDEX [IX_att_punch_events_WorkDate] ON [dbo].[att_punch_events] ([WorkDate]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_att_punch_events_CapturedAtUtc'
                    AND object_id = OBJECT_ID(N'dbo.att_punch_events'))
                    CREATE INDEX [IX_att_punch_events_CapturedAtUtc] ON [dbo].[att_punch_events] ([CapturedAtUtc]);
                """,
            _ => null
        };
        if (sql is null) return;

        // Do not swallow DDL errors: startup must fail rather than serve a broken punch endpoint.
        await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);

        // Add the display name column to pre-existing capture tables as well.
        if (provider == "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"wintech_hrms\".\"att_punch_events\" ADD COLUMN IF NOT EXISTS \"LocationName\" character varying(300) NULL",
                cancellationToken);
        }
        else if (provider == "Microsoft.EntityFrameworkCore.SqlServer")
        {
            await db.Database.ExecuteSqlRawAsync(
                "IF COL_LENGTH(N'dbo.att_punch_events', N'LocationName') IS NULL ALTER TABLE [dbo].[att_punch_events] ADD [LocationName] nvarchar(300) NULL",
                cancellationToken);
        }
        else if (provider == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            var connection = db.Database.GetDbConnection();
            var openedHere = connection.State != System.Data.ConnectionState.Open;
            if (openedHere) await db.Database.OpenConnectionAsync(cancellationToken);
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA table_info(\"att_punch_events\")";
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                var found = false;
                while (await reader.ReadAsync(cancellationToken))
                    if (string.Equals(reader.GetString(1), "LocationName", StringComparison.OrdinalIgnoreCase)) found = true;
                await reader.CloseAsync();
                if (!found)
                    await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"att_punch_events\" ADD COLUMN \"LocationName\" TEXT NULL", cancellationToken);
            }
            finally { if (openedHere) await db.Database.CloseConnectionAsync(); }
        }
        logger.LogInformation("Verified attendance punch-capture table and indexes.");
    }
}
