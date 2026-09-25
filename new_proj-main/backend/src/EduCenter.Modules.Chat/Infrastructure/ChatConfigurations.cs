using EduCenter.Modules.Chat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduCenter.Modules.Chat.Infrastructure;

public class ChatRoomConfiguration : IEntityTypeConfiguration<ChatRoom>
{
    public void Configure(EntityTypeBuilder<ChatRoom> builder)
    {
        builder.ToTable("chats");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.ChannelKind).HasConversion<string>().HasMaxLength(30);
        builder.Property(c => c.DirectKey).HasMaxLength(80);

        // One chat per group, per branch channel and per pair of users — lazy creation races resolve on these.
        builder.HasIndex(c => c.GroupId).IsUnique().HasFilter("\"GroupId\" IS NOT NULL");
        builder.HasIndex(c => new { c.BranchId, c.ChannelKind }).IsUnique().HasFilter("\"ChannelKind\" IS NOT NULL");
        builder.HasIndex(c => new { c.OrganizationId, c.DirectKey }).IsUnique().HasFilter("\"DirectKey\" IS NOT NULL");
    }
}

public class ChatMemberConfiguration : IEntityTypeConfiguration<ChatMember>
{
    public void Configure(EntityTypeBuilder<ChatMember> builder)
    {
        builder.ToTable("chat_members");
        builder.HasKey(m => m.Id);
        builder.HasIndex(m => new { m.ChatId, m.UserId }).IsUnique();
        builder.HasIndex(m => m.UserId);
    }
}

public class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        builder.ToTable("chat_messages");
        builder.HasKey(m => m.Id);
        builder.Ignore(m => m.IsDeleted);
        builder.Property(m => m.Body).HasMaxLength(ChatMessage.MaxBodyLength);
        builder.Property(m => m.AttachmentName).HasMaxLength(255);
        builder.Property(m => m.AttachmentContentType).HasMaxLength(100);
        builder.Property(m => m.AttachmentKey).HasMaxLength(100);
        builder.HasIndex(m => new { m.ChatId, m.CreatedAt });
    }
}

public class ChatReactionConfiguration : IEntityTypeConfiguration<ChatReaction>
{
    public void Configure(EntityTypeBuilder<ChatReaction> builder)
    {
        builder.ToTable("chat_reactions");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Emoji).HasMaxLength(16).IsRequired();
        builder.HasIndex(r => new { r.MessageId, r.UserId, r.Emoji }).IsUnique();
    }
}
