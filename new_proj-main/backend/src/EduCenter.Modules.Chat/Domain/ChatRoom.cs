using EduCenter.SharedKernel;

namespace EduCenter.Modules.Chat.Domain;

public enum ChatType
{
    Direct,
    Group,
    StaffChannel
}

/// <summary>Per-branch staff channels (docs/00_TZ.md §22 "Staff Channels").</summary>
public enum StaffChannelKind
{
    General,
    Teachers,
    IeltsTeachers,
    Administrators,
    Announcements
}

/// <summary>
/// A conversation. Who may read and post is not stored for group chats and staff channels — it follows the
/// group's enrollment/staffing and the branch's staff at the moment of the request, so a student who leaves a
/// group loses the chat with no membership rows to clean up. Direct chats list their two participants in
/// <see cref="ChatMember"/>.
/// </summary>
public class ChatRoom : TenantEntity
{
    private ChatRoom() { } // EF Core

    private ChatRoom(Guid organizationId, ChatType type)
    {
        OrganizationId = organizationId;
        Type = type;
    }

    public static ChatRoom ForGroup(Guid organizationId, Guid groupId, Guid branchId) =>
        new(organizationId, ChatType.Group) { GroupId = groupId, BranchId = branchId };

    public static ChatRoom ForStaffChannel(Guid organizationId, Guid branchId, StaffChannelKind kind) =>
        new(organizationId, ChatType.StaffChannel) { BranchId = branchId, ChannelKind = kind };

    public static ChatRoom Direct(Guid organizationId, Guid userA, Guid userB) =>
        new(organizationId, ChatType.Direct) { DirectKey = DirectKeyFor(userA, userB) };

    /// <summary>Order-independent key, unique per organization, so a pair of users has exactly one direct chat.</summary>
    public static string DirectKeyFor(Guid userA, Guid userB) =>
        userA.CompareTo(userB) < 0 ? $"{userA:N}:{userB:N}" : $"{userB:N}:{userA:N}";

    public ChatType Type { get; private set; }
    public Guid? GroupId { get; private set; }
    public Guid? BranchId { get; private set; }
    public StaffChannelKind? ChannelKind { get; private set; }
    public string? DirectKey { get; private set; }
    public DateTimeOffset? LastMessageAt { get; private set; }

    public void Touch(DateTimeOffset at) => LastMessageAt = at;
}

/// <summary>A participant's read cursor. For direct chats the two rows are also the membership; for group chats
/// and staff channels a row appears the first time the user reads the chat.</summary>
public class ChatMember
{
    private ChatMember() { } // EF Core

    public ChatMember(Guid chatId, Guid userId)
    {
        Id = Guid.NewGuid();
        ChatId = chatId;
        UserId = userId;
        JoinedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid ChatId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTimeOffset JoinedAt { get; private set; }
    public DateTimeOffset? LastReadAt { get; private set; }

    public void MarkRead(DateTimeOffset at)
    {
        if (LastReadAt is null || at > LastReadAt) LastReadAt = at;
    }
}
