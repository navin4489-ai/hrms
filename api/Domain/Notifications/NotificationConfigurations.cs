using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Wintech.Hrms.Api.Domain.Notifications;

public sealed class AppNotificationConfiguration : IEntityTypeConfiguration<AppNotification>
{
    public void Configure(EntityTypeBuilder<AppNotification> builder)
    {
        builder.ToTable("ntf_notifications");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Type).HasMaxLength(100).IsRequired();
        builder.Property(n => n.Title).HasMaxLength(300).IsRequired();
        builder.Property(n => n.Body).HasMaxLength(1000).IsRequired();
        builder.Property(n => n.DeepLink).HasMaxLength(500);
        builder.Property(n => n.Category).HasMaxLength(50).IsRequired();
        builder.HasIndex(n => new { n.UserId, n.ReadAt });
        builder.HasIndex(n => n.CreatedAt);
    }
}
