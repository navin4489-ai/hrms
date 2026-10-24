using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wintech.Hrms.Api.Domain.Employees;

namespace Wintech.Hrms.Api.Domain.Expenses;

public sealed class ExpenseClaimConfiguration : IEntityTypeConfiguration<ExpenseClaim>
{
    public void Configure(EntityTypeBuilder<ExpenseClaim> builder)
    {
        builder.ToTable("exp_expense_claims");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Category).HasMaxLength(50).IsRequired();
        builder.Property(c => c.Amount).HasPrecision(18, 2);
        builder.Property(c => c.Currency).HasMaxLength(3).IsRequired();
        builder.Property(c => c.BusinessPurpose).HasMaxLength(500).IsRequired();
        builder.Property(c => c.CostCentre).HasMaxLength(50);
        builder.Property(c => c.PaidReference).HasMaxLength(100);
        builder.Property(c => c.RejectionReason).HasMaxLength(1000);
        builder.Property(c => c.ReceiptStorageKey).HasMaxLength(400);
        builder.Property(c => c.ReceiptFileName).HasMaxLength(260);
        builder.Property(c => c.ReceiptContentType).HasMaxLength(100);
        builder.Property(c => c.ReceiptSha256).HasMaxLength(64);
        builder.HasIndex(c => c.EmployeeId);
        builder.HasIndex(c => c.Status);
        builder.HasIndex(c => c.SubmittedAt);
        builder.HasIndex(c => c.ReceiptSha256);
        builder.HasIndex(c => c.ApprovalTaskId);

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(c => c.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TravelRequestConfiguration : IEntityTypeConfiguration<TravelRequest>
{
    public void Configure(EntityTypeBuilder<TravelRequest> builder)
    {
        builder.ToTable("exp_travel_requests");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Destination).HasMaxLength(200).IsRequired();
        builder.Property(t => t.Purpose).HasMaxLength(500).IsRequired();
        builder.Property(t => t.EstimatedCost).HasPrecision(18, 2);
        builder.Property(t => t.Currency).HasMaxLength(3).IsRequired();
        builder.Property(t => t.RejectionReason).HasMaxLength(1000);
        builder.HasIndex(t => t.EmployeeId);
        builder.HasIndex(t => t.Status);
        builder.HasIndex(t => t.ApprovalTaskId);

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(t => t.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
