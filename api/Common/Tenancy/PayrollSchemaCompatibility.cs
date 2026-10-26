using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Data;

namespace Wintech.Hrms.Api.Common.Tenancy;

/// <summary>
/// Additive upgrade for databases created before the automatic payroll/tax module existed.
/// The production database was first created with EnsureCreated, which never adds new tables,
/// so the employee pay-details table must be created here or salary endpoints fail with 500.
/// Idempotent: CREATE TABLE IF NOT EXISTS. Provider-aware.
/// </summary>
public static class PayrollSchemaCompatibility
{
    public static async Task EnsureAsync(AppDbContext db, ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var provider = db.Database.ProviderName;
        var sql = provider switch
        {
            "Npgsql.EntityFrameworkCore.PostgreSQL" => """
                CREATE TABLE IF NOT EXISTS "wintech_hrms"."pay_employee_salary_structures" (
                    "Id" uuid NOT NULL PRIMARY KEY,
                    "EmployeeId" uuid NOT NULL REFERENCES "wintech_hrms"."Employees" ("Id") ON DELETE CASCADE,
                    "BasicAnnual" numeric(18,2) NOT NULL,
                    "HraAnnual" numeric(18,2) NOT NULL,
                    "DaAnnual" numeric(18,2) NOT NULL,
                    "SpecialAllowanceAnnual" numeric(18,2) NOT NULL,
                    "OtherAllowanceAnnual" numeric(18,2) NOT NULL,
                    "EmployerPfAnnual" numeric(18,2) NOT NULL,
                    "GratuityAnnual" numeric(18,2) NOT NULL,
                    "ProfessionalTaxMonthly" numeric(18,2) NOT NULL,
                    "TaxRegime" integer NOT NULL,
                    "Deduction80CAnnual" numeric(18,2) NOT NULL,
                    "Deduction80DAnnual" numeric(18,2) NOT NULL,
                    "HomeLoanInterestAnnual" numeric(18,2) NOT NULL,
                    "OtherDeductionsAnnual" numeric(18,2) NOT NULL,
                    "Currency" character varying(10) NOT NULL,
                    "UpdatedAt" timestamp with time zone NULL,
                    "UpdatedByUserId" uuid NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_pay_employee_salary_structures_EmployeeId"
                    ON "wintech_hrms"."pay_employee_salary_structures" ("EmployeeId");
                """,
            "Microsoft.EntityFrameworkCore.Sqlite" => """
                CREATE TABLE IF NOT EXISTS "pay_employee_salary_structures" (
                    "Id" TEXT NOT NULL PRIMARY KEY,
                    "EmployeeId" TEXT NOT NULL REFERENCES "Employees" ("Id") ON DELETE CASCADE,
                    "BasicAnnual" REAL NOT NULL,
                    "HraAnnual" REAL NOT NULL,
                    "DaAnnual" REAL NOT NULL,
                    "SpecialAllowanceAnnual" REAL NOT NULL,
                    "OtherAllowanceAnnual" REAL NOT NULL,
                    "EmployerPfAnnual" REAL NOT NULL,
                    "GratuityAnnual" REAL NOT NULL,
                    "ProfessionalTaxMonthly" REAL NOT NULL,
                    "TaxRegime" INTEGER NOT NULL,
                    "Deduction80CAnnual" REAL NOT NULL,
                    "Deduction80DAnnual" REAL NOT NULL,
                    "HomeLoanInterestAnnual" REAL NOT NULL,
                    "OtherDeductionsAnnual" REAL NOT NULL,
                    "Currency" TEXT NOT NULL,
                    "UpdatedAt" TEXT NULL,
                    "UpdatedByUserId" TEXT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_pay_employee_salary_structures_EmployeeId"
                    ON "pay_employee_salary_structures" ("EmployeeId");
                """,
            "Microsoft.EntityFrameworkCore.SqlServer" => """
                IF OBJECT_ID(N'dbo.pay_employee_salary_structures', N'U') IS NULL
                    CREATE TABLE [dbo].[pay_employee_salary_structures] (
                        [Id] uniqueidentifier NOT NULL PRIMARY KEY,
                        [EmployeeId] uniqueidentifier NOT NULL REFERENCES [dbo].[Employees] ([Id]) ON DELETE CASCADE,
                        [BasicAnnual] numeric(18,2) NOT NULL,
                        [HraAnnual] numeric(18,2) NOT NULL,
                        [DaAnnual] numeric(18,2) NOT NULL,
                        [SpecialAllowanceAnnual] numeric(18,2) NOT NULL,
                        [OtherAllowanceAnnual] numeric(18,2) NOT NULL,
                        [EmployerPfAnnual] numeric(18,2) NOT NULL,
                        [GratuityAnnual] numeric(18,2) NOT NULL,
                        [ProfessionalTaxMonthly] numeric(18,2) NOT NULL,
                        [TaxRegime] int NOT NULL,
                        [Deduction80CAnnual] numeric(18,2) NOT NULL,
                        [Deduction80DAnnual] numeric(18,2) NOT NULL,
                        [HomeLoanInterestAnnual] numeric(18,2) NOT NULL,
                        [OtherDeductionsAnnual] numeric(18,2) NOT NULL,
                        [Currency] nvarchar(10) NOT NULL,
                        [UpdatedAt] datetime2 NULL,
                        [UpdatedByUserId] uniqueidentifier NULL
                    );
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_pay_employee_salary_structures_EmployeeId'
                    AND object_id = OBJECT_ID(N'dbo.pay_employee_salary_structures'))
                    CREATE UNIQUE INDEX [IX_pay_employee_salary_structures_EmployeeId]
                        ON [dbo].[pay_employee_salary_structures] ([EmployeeId]);
                """,
            _ => null
        };
        if (sql is null) return;

        // Do not swallow DDL errors: startup must fail rather than serve a broken payroll endpoint.
        await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        logger.LogInformation("Verified employee pay-details table and unique index.");
    }
}
