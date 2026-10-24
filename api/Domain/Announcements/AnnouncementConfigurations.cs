using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Wintech.Hrms.Api.Domain.Announcements;

public sealed class AnnouncementConfiguration : IEntityTypeConfiguration<Announcement>
{
    public void Configure(EntityTypeBuilder<Announcement> builder)
    {
        builder.ToTable("ann_announcements");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Title).HasMaxLength(200).IsRequired();
        builder.Property(a => a.Summary).HasMaxLength(500).IsRequired();
        builder.Property(a => a.Category).HasConversion<string>().HasMaxLength(30);
        builder.Property(a => a.Audience).HasConversion<string>().HasMaxLength(30);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.CreatedByName).HasMaxLength(200);
        builder.HasIndex(a => a.Status);
        builder.HasIndex(a => a.PublishedAt);

        builder.HasMany(a => a.Targets)
            .WithOne(t => t.Announcement)
            .HasForeignKey(t => t.AnnouncementId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(a => a.Acknowledgements)
            .WithOne(x => x.Announcement)
            .HasForeignKey(x => x.AnnouncementId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class AnnouncementTargetConfiguration : IEntityTypeConfiguration<AnnouncementTarget>
{
    public void Configure(EntityTypeBuilder<AnnouncementTarget> builder)
    {
        builder.ToTable("ann_targets");
        builder.HasKey(t => t.Id);
        builder.HasIndex(t => new { t.AnnouncementId, t.TargetId }).IsUnique();
    }
}

public sealed class AnnouncementAcknowledgementConfiguration : IEntityTypeConfiguration<AnnouncementAcknowledgement>
{
    public void Configure(EntityTypeBuilder<AnnouncementAcknowledgement> builder)
    {
        builder.ToTable("ann_acknowledgements");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.AnnouncementId, x.UserId }).IsUnique();
        builder.HasIndex(x => x.UserId);
    }
}
