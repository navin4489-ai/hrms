using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wintech.Hrms.Api.Domain.Audit;
using Wintech.Hrms.Api.Domain.Approvals;
using Wintech.Hrms.Api.Domain.Employees;
using Wintech.Hrms.Api.Domain.Identity;
using Wintech.Hrms.Api.Domain.Organization;

namespace Wintech.Hrms.Api.Data.Configurations;

public sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("Organizations");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Name).HasMaxLength(200).IsRequired();
        builder.Property(o => o.Code).HasMaxLength(50).IsRequired();
        builder.HasIndex(o => o.Code).IsUnique();
        builder.Property(o => o.Country).HasMaxLength(2);
        builder.Property(o => o.TimeZone).HasMaxLength(100);
        builder.Property(o => o.Currency).HasMaxLength(10);
    }
}

public sealed class OrgUnitConfiguration : IEntityTypeConfiguration<OrgUnit>
{
    public void Configure(EntityTypeBuilder<OrgUnit> builder)
    {
        builder.ToTable("OrgUnits");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Code).HasMaxLength(50).IsRequired();
        builder.Property(u => u.Name).HasMaxLength(200).IsRequired();
        builder.HasIndex(u => u.Code).IsUnique();
        builder.HasIndex(u => u.Type);
        builder.Property(u => u.ManagerId);
        builder.HasOne(u => u.Parent)
            .WithMany(u => u.Children)
            .HasForeignKey(u => u.ParentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.ToTable("Locations");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Code).HasMaxLength(50).IsRequired();
        builder.Property(l => l.Name).HasMaxLength(200).IsRequired();
        builder.Property(l => l.City).HasMaxLength(100).IsRequired();
        builder.Property(l => l.Country).HasMaxLength(2);
        builder.Property(l => l.TimeZone).HasMaxLength(100);
        builder.HasIndex(l => l.Code).IsUnique();
    }
}

public sealed class DesignationConfiguration : IEntityTypeConfiguration<Designation>
{
    public void Configure(EntityTypeBuilder<Designation> builder)
    {
        builder.ToTable("Designations");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Code).HasMaxLength(50).IsRequired();
        builder.Property(d => d.Name).HasMaxLength(200).IsRequired();
        builder.Property(d => d.Grade).HasMaxLength(50);
        builder.HasIndex(d => d.Code).IsUnique();
    }
}

public sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.ToTable("Employees");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.EmployeeNumber).HasMaxLength(50).IsRequired();
        builder.HasIndex(e => e.EmployeeNumber).IsUnique();
        builder.Property(e => e.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(e => e.LastName).HasMaxLength(100).IsRequired();
        builder.Property(e => e.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.WorkEmail).HasMaxLength(320).IsRequired();
        builder.HasIndex(e => e.WorkEmail).IsUnique();
        builder.Property(e => e.Phone).HasMaxLength(50);
        builder.Property(e => e.CostCentre).HasMaxLength(50);
        builder.Property(e => e.PhotoStorageKey).HasMaxLength(400);
        builder.HasIndex(e => e.EmploymentStatus);
        builder.HasIndex(e => e.DepartmentId);
        builder.HasIndex(e => e.ManagerId);

        builder.HasOne(e => e.Organization)
            .WithMany()
            .HasForeignKey(e => e.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Manager)
            .WithMany()
            .HasForeignKey(e => e.ManagerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Designation)
            .WithMany()
            .HasForeignKey(e => e.DesignationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Department)
            .WithMany()
            .HasForeignKey(e => e.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Location)
            .WithMany()
            .HasForeignKey(e => e.LocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.PersonalDetail)
            .WithOne(p => p.Employee)
            .HasForeignKey<EmployeePersonalDetail>(p => p.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.SensitiveData)
            .WithOne(s => s.Employee)
            .HasForeignKey<EmployeeSensitiveData>(s => s.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.EmploymentHistory)
            .WithOne(h => h.Employee!)
            .HasForeignKey(h => h.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.ProfileChangeRequests)
            .WithOne(r => r.Employee!)
            .HasForeignKey(r => r.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class EmployeePersonalDetailConfiguration : IEntityTypeConfiguration<EmployeePersonalDetail>
{
    public void Configure(EntityTypeBuilder<EmployeePersonalDetail> builder)
    {
        builder.ToTable("EmployeePersonalDetails");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.PreferredName).HasMaxLength(100);
        builder.Property(p => p.PersonalEmail).HasMaxLength(320);
        builder.Property(p => p.PersonalPhone).HasMaxLength(50);
        builder.Property(p => p.Address).HasMaxLength(500);
        builder.Property(p => p.City).HasMaxLength(100);
        builder.Property(p => p.PostalCode).HasMaxLength(20);
        builder.Property(p => p.EmergencyContactName).HasMaxLength(200);
        builder.Property(p => p.EmergencyContactPhone).HasMaxLength(50);
        builder.HasIndex(p => p.EmployeeId).IsUnique();
    }
}

public sealed class EmployeeSensitiveDataConfiguration : IEntityTypeConfiguration<EmployeeSensitiveData>
{
    public void Configure(EntityTypeBuilder<EmployeeSensitiveData> builder)
    {
        builder.ToTable("EmployeeSensitiveData");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.BankAccountNumber).HasMaxLength(50);
        builder.Property(s => s.BankName).HasMaxLength(200);
        builder.Property(s => s.BankBranch).HasMaxLength(200);
        builder.Property(s => s.NationalIdType).HasMaxLength(50);
        builder.Property(s => s.NationalIdNumber).HasMaxLength(100);
        builder.Property(s => s.TaxId).HasMaxLength(100);
        builder.HasIndex(s => s.EmployeeId).IsUnique();
    }
}

public sealed class EmploymentHistoryConfiguration : IEntityTypeConfiguration<EmploymentHistory>
{
    public void Configure(EntityTypeBuilder<EmploymentHistory> builder)
    {
        builder.ToTable("EmploymentHistory");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.OldValue).HasMaxLength(500);
        builder.Property(h => h.NewValue).HasMaxLength(500);
        builder.Property(h => h.Reason).HasMaxLength(500);
        builder.Property(h => h.ApprovedBy).HasMaxLength(200);
        builder.HasIndex(h => h.EmployeeId);
        builder.HasIndex(h => h.EffectiveDate);
    }
}

public sealed class ProfileChangeRequestConfiguration : IEntityTypeConfiguration<ProfileChangeRequest>
{
    public void Configure(EntityTypeBuilder<ProfileChangeRequest> builder)
    {
        builder.ToTable("ProfileChangeRequests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Field).HasMaxLength(100).IsRequired();
        builder.Property(r => r.RequestedValue).HasMaxLength(1000).IsRequired();
        builder.Property(r => r.Reason).HasMaxLength(1000);
        builder.Property(r => r.DecisionComment).HasMaxLength(1000);
        builder.HasIndex(r => new { r.EmployeeId, r.Status });
    }
}

public sealed class IdentityConfiguration :
    IEntityTypeConfiguration<UserAccount>,
    IEntityTypeConfiguration<Role>,
    IEntityTypeConfiguration<UserRole>,
    IEntityTypeConfiguration<RolePermission>,
    IEntityTypeConfiguration<RefreshToken>,
    IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<UserAccount> builder)
    {
        builder.ToTable("UserAccounts");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.UserName).HasMaxLength(100).IsRequired();
        builder.HasIndex(u => u.UserName).IsUnique();
        builder.Property(u => u.Email).HasMaxLength(320);
        builder.HasIndex(u => u.Email).IsUnique().HasFilter("[Email] IS NOT NULL");
        builder.Property(u => u.PasswordHash).HasMaxLength(500).IsRequired();
        builder.Property(u => u.MfaSecret).HasMaxLength(200);
        builder.HasIndex(u => u.EmployeeId);

        builder.HasOne(u => u.Employee)
            .WithMany()
            .HasForeignKey(u => u.EmployeeId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(u => u.UserRoles)
            .WithOne(r => r.User!)
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(u => u.RefreshTokens)
            .WithOne(t => t.User!)
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(r => r.Name).IsUnique();
        builder.Property(r => r.Description).HasMaxLength(500);

        builder.HasMany(r => r.RolePermissions)
            .WithOne(p => p.Role!)
            .HasForeignKey(p => p.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("UserRoles");
        builder.HasKey(r => new { r.UserId, r.RoleId });
        builder.HasOne(r => r.Role)
            .WithMany(r => r.UserRoles)
            .HasForeignKey(r => r.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("RolePermissions");
        builder.HasKey(p => new { p.RoleId, p.PermissionCode });
        builder.Property(p => p.PermissionCode).HasMaxLength(100);
    }

    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.TokenHash).HasMaxLength(100).IsRequired();
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => t.FamilyId);
        builder.HasIndex(t => new { t.UserId, t.RevokedAt });
        builder.Property(t => t.DeviceName).HasMaxLength(200);
        builder.Property(t => t.IpAddress).HasMaxLength(60);
    }

    public void Configure(EntityTypeBuilder<PasswordResetToken> builder)
    {
        builder.ToTable("PasswordResetTokens");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.TokenHash).HasMaxLength(100).IsRequired();
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => t.UserId);
    }
}

public sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("AuditEvents");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.ActorEmployeeNumber).HasMaxLength(50);
        builder.Property(a => a.ActorName).HasMaxLength(200).IsRequired();
        builder.Property(a => a.Action).HasMaxLength(100).IsRequired();
        builder.Property(a => a.EntityType).HasMaxLength(100);
        builder.Property(a => a.EntityId).HasMaxLength(100);
        builder.Property(a => a.Outcome).HasMaxLength(50);
        builder.Property(a => a.CorrelationId).HasMaxLength(64);
        builder.Property(a => a.IpAddress).HasMaxLength(60);
        builder.HasIndex(a => a.Timestamp);
        builder.HasIndex(a => a.ActorUserId);
        builder.HasIndex(a => a.Action);
        builder.HasIndex(a => new { a.EntityType, a.EntityId });
    }
}

public sealed class ApprovalConfiguration :
    IEntityTypeConfiguration<ApprovalWorkflowDefinition>,
    IEntityTypeConfiguration<ApprovalWorkflowStep>,
    IEntityTypeConfiguration<ApprovalTask>,
    IEntityTypeConfiguration<ApprovalAction>
{
    public void Configure(EntityTypeBuilder<ApprovalWorkflowDefinition> builder)
    {
        builder.ToTable("ApprovalWorkflowDefinitions");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Code).HasMaxLength(50).IsRequired();
        builder.Property(w => w.Name).HasMaxLength(200).IsRequired();
        builder.Property(w => w.RequestType).HasMaxLength(100).IsRequired();
        builder.HasIndex(w => new { w.Code, w.Version }).IsUnique();
        builder.HasIndex(w => new { w.RequestType, w.IsActive });

        builder.HasMany(w => w.Steps)
            .WithOne(s => s.Workflow!)
            .HasForeignKey(s => s.WorkflowId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<ApprovalWorkflowStep> builder)
    {
        builder.ToTable("ApprovalWorkflowSteps");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
        builder.Property(s => s.ApproverValue).HasMaxLength(200);
        builder.HasIndex(s => new { s.WorkflowId, s.StepNumber }).IsUnique();
    }

    public void Configure(EntityTypeBuilder<ApprovalTask> builder)
    {
        builder.ToTable("ApprovalTasks");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.RequestType).HasMaxLength(100).IsRequired();
        builder.Property(t => t.Title).HasMaxLength(400).IsRequired();
        builder.Property(t => t.RequesterName).HasMaxLength(200);
        builder.Property(t => t.Status).HasMaxLength(50).IsRequired();
        builder.Property(t => t.CurrentStepName).HasMaxLength(200);
        builder.Property(t => t.CurrentAssigneeRole).HasMaxLength(100);
        builder.Property(t => t.Currency).HasMaxLength(10);
        builder.Property(t => t.Amount).HasPrecision(18, 2);
        builder.Property(t => t.Version).IsConcurrencyToken();
        builder.HasIndex(t => new { t.RequestType, t.RequestId });
        builder.HasIndex(t => new { t.Status, t.CurrentAssigneeUserId });
        builder.HasIndex(t => t.RequesterUserId);
        builder.HasIndex(t => t.DueAt);

        builder.HasMany(t => t.Actions)
            .WithOne(a => a.Task!)
            .HasForeignKey(a => a.TaskId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<ApprovalAction> builder)
    {
        builder.ToTable("ApprovalActions");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.ActorName).HasMaxLength(200).IsRequired();
        builder.Property(a => a.Comment).HasMaxLength(2000);
        builder.HasIndex(a => a.TaskId);
        builder.HasIndex(a => a.At);
    }
}
