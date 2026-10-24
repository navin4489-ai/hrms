using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Wintech.Hrms.Api.Domain.Cases;

public sealed class CaseCategoryConfiguration : IEntityTypeConfiguration<CaseCategory>
{
    public void Configure(EntityTypeBuilder<CaseCategory> builder)
    {
        builder.ToTable("cs_categories");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(c => c.Name).IsUnique();
        builder.Property(c => c.IsActive).HasDefaultValue(true);
    }
}

public sealed class CaseSubcategoryConfiguration : IEntityTypeConfiguration<CaseSubcategory>
{
    public void Configure(EntityTypeBuilder<CaseSubcategory> builder)
    {
        builder.ToTable("cs_subcategories");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(s => new { s.CategoryId, s.Name }).IsUnique();
        builder.HasOne(s => s.Category)
            .WithMany(c => c.Subcategories)
            .HasForeignKey(s => s.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class HelpdeskCaseConfiguration : IEntityTypeConfiguration<HelpdeskCase>
{
    public void Configure(EntityTypeBuilder<HelpdeskCase> builder)
    {
        builder.ToTable("cs_cases");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.TicketNumber).HasMaxLength(30).IsRequired();
        builder.HasIndex(c => c.TicketNumber).IsUnique();
        builder.Property(c => c.Subject).HasMaxLength(300).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(4000).IsRequired();
        builder.Property(c => c.Priority).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(c => c.Queue).HasMaxLength(50).IsRequired();
        builder.Property(c => c.ResolutionCode).HasMaxLength(100);
        builder.HasIndex(c => c.Status);
        builder.HasIndex(c => c.EmployeeId);
        builder.HasIndex(c => c.Queue);

        builder.HasOne(c => c.Category)
            .WithMany()
            .HasForeignKey(c => c.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.Subcategory)
            .WithMany()
            .HasForeignKey(c => c.SubcategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.Employee)
            .WithMany()
            .HasForeignKey(c => c.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.Assignee)
            .WithMany()
            .HasForeignKey(c => c.AssigneeEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CaseMessageConfiguration : IEntityTypeConfiguration<CaseMessage>
{
    public void Configure(EntityTypeBuilder<CaseMessage> builder)
    {
        builder.ToTable("cs_case_messages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.SenderName).HasMaxLength(200).IsRequired();
        builder.Property(m => m.SenderRole).HasMaxLength(50).IsRequired();
        builder.Property(m => m.Visibility).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.Body).HasMaxLength(4000).IsRequired();
        builder.Property(m => m.AttachmentUrl).HasMaxLength(500);
        builder.HasIndex(m => m.CaseId);
        builder.HasOne(m => m.Case)
            .WithMany(c => c.Messages)
            .HasForeignKey(m => m.CaseId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CaseKnowledgeArticleConfiguration : IEntityTypeConfiguration<CaseKnowledgeArticle>
{
    public void Configure(EntityTypeBuilder<CaseKnowledgeArticle> builder)
    {
        builder.ToTable("cs_knowledge_articles");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Title).HasMaxLength(200).IsRequired();
        builder.Property(a => a.Summary).HasMaxLength(500).IsRequired();
        builder.Property(a => a.Category).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Tags).HasMaxLength(300).IsRequired();
        builder.Property(a => a.Version).HasMaxLength(20).IsRequired();
        builder.HasIndex(a => a.Category);
    }
}
