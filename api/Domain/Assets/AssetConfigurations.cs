using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wintech.Hrms.Api.Domain.Employees;

namespace Wintech.Hrms.Api.Domain.Assets;

public sealed class AssetConfiguration : IEntityTypeConfiguration<Asset>
{
    public void Configure(EntityTypeBuilder<Asset> builder)
    {
        builder.ToTable("ast_assets");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Name).HasMaxLength(200).IsRequired();
        builder.Property(a => a.AssetTag).HasMaxLength(50).IsRequired();
        builder.HasIndex(a => a.AssetTag).IsUnique();
        builder.Property(a => a.SerialNumber).HasMaxLength(100);
        builder.Property(a => a.Model).HasMaxLength(100);
        builder.Property(a => a.Condition).HasMaxLength(100);
        builder.Property(a => a.Accessories).HasMaxLength(500);
        builder.Property(a => a.AcknowledgementNote).HasMaxLength(500);
        builder.Property(a => a.ReturnRequestReason).HasMaxLength(500);
        builder.Property(a => a.ReturnCondition).HasMaxLength(100);
        builder.HasIndex(a => a.Status);
        builder.HasIndex(a => a.CustodianEmployeeId);

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(a => a.CustodianEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
