using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wintech.Hrms.Api.Domain.Documents;

namespace Wintech.Hrms.Api.Domain.Documents;

// Table names carry the doc_ prefix so document tables never collide with other modules
// when the modular monolith is assembled (see AGENT_GUIDE.md, "Table names").
public sealed class EmployeeDocumentConfiguration : IEntityTypeConfiguration<EmployeeDocument>
{
    public void Configure(EntityTypeBuilder<EmployeeDocument> builder)
    {
        builder.ToTable("doc_employee_documents");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Name).HasMaxLength(260).IsRequired();
        builder.Property(d => d.DocumentType).HasMaxLength(100).IsRequired();
        builder.Property(d => d.VerificationNote).HasMaxLength(1000);
        builder.Property(d => d.FileExtension).HasMaxLength(20).IsRequired();
        builder.Property(d => d.StorageKey).HasMaxLength(400).IsRequired();
        builder.Property(d => d.ContentType).HasMaxLength(150);
        builder.HasIndex(d => d.EmployeeId);
        builder.HasIndex(d => d.Status);
        builder.HasIndex(d => d.ExpiresOn);

        builder.HasOne(d => d.Employee)
            .WithMany()
            .HasForeignKey(d => d.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class LetterRequestConfiguration : IEntityTypeConfiguration<LetterRequest>
{
    public void Configure(EntityTypeBuilder<LetterRequest> builder)
    {
        builder.ToTable("doc_letter_requests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.LetterType).HasMaxLength(50).IsRequired();
        builder.Property(r => r.Purpose).HasMaxLength(500).IsRequired();
        builder.Property(r => r.Notes).HasMaxLength(2000);
        builder.Property(r => r.DecisionComment).HasMaxLength(2000);
        builder.HasIndex(r => r.EmployeeId);
        builder.HasIndex(r => r.Status);
        builder.HasIndex(r => r.ApprovalTaskId);

        builder.HasOne(r => r.Employee)
            .WithMany()
            .HasForeignKey(r => r.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CompanyPolicyConfiguration : IEntityTypeConfiguration<CompanyPolicy>
{
    public void Configure(EntityTypeBuilder<CompanyPolicy> builder)
    {
        builder.ToTable("doc_company_policies");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Title).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Category).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Summary).HasMaxLength(1000);
        builder.Property(p => p.Body).HasMaxLength(8000);
        builder.Property(p => p.Version).HasMaxLength(20).IsRequired();
        builder.HasIndex(p => p.IsActive);
        builder.HasIndex(p => p.Category);
    }
}

public sealed class PolicyAcknowledgementConfiguration : IEntityTypeConfiguration<PolicyAcknowledgement>
{
    public void Configure(EntityTypeBuilder<PolicyAcknowledgement> builder)
    {
        builder.ToTable("doc_policy_acknowledgements");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.PolicyVersion).HasMaxLength(20).IsRequired();
        builder.Property(a => a.Comment).HasMaxLength(1000);
        // One acknowledgement per employee per policy version (FR-EMP-074: versioned, 409 on repeat).
        builder.HasIndex(a => new { a.PolicyId, a.EmployeeId, a.PolicyVersion }).IsUnique();
        builder.HasIndex(a => a.EmployeeId);

        builder.HasOne(a => a.Policy)
            .WithMany()
            .HasForeignKey(a => a.PolicyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
