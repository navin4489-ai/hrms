using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wintech.Hrms.Api.Domain.Identity;

namespace Wintech.Hrms.Api.Domain.Admin;

public sealed class SystemConfigurationConfiguration : IEntityTypeConfiguration<SystemConfiguration>
{
    public void Configure(EntityTypeBuilder<SystemConfiguration> builder)
    {
        builder.ToTable("adm_configuration");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Key).HasMaxLength(200).IsRequired();
        builder.HasIndex(c => c.Key).IsUnique();
        builder.Property(c => c.Value).HasMaxLength(4000).IsRequired();
        builder.Property(c => c.Type).HasMaxLength(20).IsRequired();
        builder.HasIndex(c => c.Type);
        builder.Property(c => c.Description).HasMaxLength(500);

        builder.HasOne(c => c.UpdatedByUser)
            .WithMany()
            .HasForeignKey(c => c.UpdatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
