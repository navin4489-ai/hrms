using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wintech.Hrms.Api.Domain.Profile;

namespace Wintech.Hrms.Api.Domain.Profile.Configurations;

public sealed class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.ToTable("hr_notification_preferences");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.UserId).IsRequired();
        builder.Property(p => p.Category).HasMaxLength(100).IsRequired();
        builder.HasIndex(p => new { p.UserId, p.Category }).IsUnique();
    }
}
