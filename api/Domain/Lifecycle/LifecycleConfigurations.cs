using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wintech.Hrms.Api.Domain.Employees;

namespace Wintech.Hrms.Api.Domain.Lifecycle;

/// <summary>
/// EF configurations for every Lifecycle entity. Table names use the assigned <c>lc_</c> prefix
/// so they never collide with other modules when the solution is assembled.
/// </summary>
public sealed class ResignationConfiguration : IEntityTypeConfiguration<Resignation>
{
    public void Configure(EntityTypeBuilder<Resignation> builder)
    {
        builder.ToTable("lc_resignations");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Reason).HasMaxLength(1000).IsRequired();
        builder.Property(r => r.Comments).HasMaxLength(2000);
        builder.Property(r => r.WithdrawReason).HasMaxLength(1000);
        builder.HasIndex(r => new { r.EmployeeId, r.Status });
        builder.HasIndex(r => r.SubmittedAt);

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(r => r.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ResignationEventConfiguration : IEntityTypeConfiguration<ResignationEvent>
{
    public void Configure(EntityTypeBuilder<ResignationEvent> builder)
    {
        builder.ToTable("lc_resignation_events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Step).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Action).HasMaxLength(100).IsRequired();
        builder.Property(e => e.ActorName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Comment).HasMaxLength(2000);
        builder.HasIndex(e => new { e.ResignationId, e.At });
    }
}

public sealed class ExitChecklistItemConfiguration : IEntityTypeConfiguration<ExitChecklistItem>
{
    public void Configure(EntityTypeBuilder<ExitChecklistItem> builder)
    {
        builder.ToTable("lc_exit_checklist_items");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Note).HasMaxLength(1000);
        builder.Property(i => i.AttachmentUrl).HasMaxLength(500);
        builder.HasIndex(i => new { i.ResignationId, i.Task }).IsUnique();
        builder.HasIndex(i => new { i.ResignationId, i.Status });
    }
}

public sealed class ExitSurveyConfiguration : IEntityTypeConfiguration<ExitSurvey>
{
    public void Configure(EntityTypeBuilder<ExitSurvey> builder)
    {
        builder.ToTable("lc_exit_surveys");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Likes).HasMaxLength(2000);
        builder.Property(s => s.Improvements).HasMaxLength(2000);
        builder.HasIndex(s => s.ResignationId).IsUnique();
    }
}

public sealed class LifecycleChecklistConfiguration : IEntityTypeConfiguration<LifecycleChecklist>
{
    public void Configure(EntityTypeBuilder<LifecycleChecklist> builder)
    {
        builder.ToTable("lc_checklists");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.TemplateCode).HasMaxLength(50).IsRequired();
        builder.Property(c => c.Status).HasMaxLength(20).IsRequired();
        builder.HasIndex(c => new { c.EmployeeId, c.Type, c.Status });
        builder.HasIndex(c => c.Type);
    }
}

public sealed class LifecycleChecklistTaskConfiguration : IEntityTypeConfiguration<LifecycleChecklistTask>
{
    public void Configure(EntityTypeBuilder<LifecycleChecklistTask> builder)
    {
        builder.ToTable("lc_checklist_tasks");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Code).HasMaxLength(50).IsRequired();
        builder.Property(t => t.Title).HasMaxLength(300).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(1000);
        builder.Property(t => t.CompletedBy).HasMaxLength(200);
        builder.Property(t => t.Note).HasMaxLength(1000);
        builder.HasIndex(t => new { t.ChecklistId, t.Order });
        builder.HasIndex(t => t.Status);

        builder.HasOne<LifecycleChecklist>()
            .WithMany()
            .HasForeignKey(t => t.ChecklistId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class LifecycleChecklistEventConfiguration : IEntityTypeConfiguration<LifecycleChecklistEvent>
{
    public void Configure(EntityTypeBuilder<LifecycleChecklistEvent> builder)
    {
        builder.ToTable("lc_checklist_events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Action).HasMaxLength(100).IsRequired();
        builder.Property(e => e.ActorName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Note).HasMaxLength(1000);
        builder.HasIndex(e => new { e.ChecklistId, e.At });

        builder.HasOne<LifecycleChecklist>()
            .WithMany()
            .HasForeignKey(e => e.ChecklistId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class LifecycleTemplateConfiguration : IEntityTypeConfiguration<LifecycleTemplate>
{
    public void Configure(EntityTypeBuilder<LifecycleTemplate> builder)
    {
        builder.ToTable("lc_templates");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Code).HasMaxLength(50).IsRequired();
        builder.HasIndex(t => t.Code).IsUnique();
        builder.Property(t => t.Name).HasMaxLength(200).IsRequired();

        builder.HasMany(t => t.Tasks)
            .WithOne()
            .HasForeignKey("TemplateId")
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class LifecycleTemplateTaskConfiguration : IEntityTypeConfiguration<LifecycleTemplateTask>
{
    public void Configure(EntityTypeBuilder<LifecycleTemplateTask> builder)
    {
        builder.ToTable("lc_template_tasks");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Code).HasMaxLength(50).IsRequired();
        builder.Property(t => t.Title).HasMaxLength(300).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(1000);
        builder.HasIndex(t => new { t.TemplateId, t.Order });
    }
}

public sealed class LifecycleSettingConfiguration : IEntityTypeConfiguration<LifecycleSetting>
{
    public void Configure(EntityTypeBuilder<LifecycleSetting> builder)
    {
        builder.ToTable("lc_config");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Key).HasMaxLength(100).IsRequired();
        builder.HasIndex(s => s.Key).IsUnique();
        builder.Property(s => s.Value).HasMaxLength(400).IsRequired();
        builder.Property(s => s.Description).HasMaxLength(500);
    }
}
