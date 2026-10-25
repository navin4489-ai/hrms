using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Wintech.Hrms.Api.Domain.Chat;

public sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("chat_conversations");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).HasMaxLength(200);
        builder.Property(c => c.DirectKey).HasMaxLength(100);
        builder.Property(c => c.LastMessagePreview).HasMaxLength(300);
        builder.HasIndex(c => new { c.OrganizationId, c.DirectKey }).IsUnique()
            .HasFilter(null);
        builder.HasIndex(c => c.LastMessageAt);

        builder.HasMany(c => c.Members)
            .WithOne(m => m.Conversation!)
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.Messages)
            .WithOne(m => m.Conversation!)
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ConversationMemberConfiguration : IEntityTypeConfiguration<ConversationMember>
{
    public void Configure(EntityTypeBuilder<ConversationMember> builder)
    {
        builder.ToTable("chat_members");
        builder.HasKey(m => m.Id);
        builder.HasIndex(m => new { m.ConversationId, m.EmployeeId }).IsUnique();
        builder.HasIndex(m => m.EmployeeId);
    }
}

public sealed class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        builder.ToTable("chat_messages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Body).HasMaxLength(4000);
        builder.Property(m => m.AttachmentStorageKey).HasMaxLength(400);
        builder.Property(m => m.AttachmentFileName).HasMaxLength(260);
        builder.Property(m => m.AttachmentContentType).HasMaxLength(150);
        builder.HasIndex(m => new { m.ConversationId, m.CreatedAt });
    }
}

public sealed class CallSessionConfiguration : IEntityTypeConfiguration<CallSession>
{
    public void Configure(EntityTypeBuilder<CallSession> builder)
    {
        builder.ToTable("chat_calls");
        builder.HasKey(c => c.Id);
        builder.HasIndex(c => new { c.CalleeEmployeeId, c.State });
        builder.HasIndex(c => c.StartedAt);
    }
}

public sealed class DeviceTokenConfiguration : IEntityTypeConfiguration<DeviceToken>
{
    public void Configure(EntityTypeBuilder<DeviceToken> builder)
    {
        builder.ToTable("chat_device_tokens");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Token).HasMaxLength(400).IsRequired();
        builder.Property(d => d.Platform).HasMaxLength(20);
        builder.HasIndex(d => d.Token).IsUnique();
        builder.HasIndex(d => d.EmployeeId);
    }
}
