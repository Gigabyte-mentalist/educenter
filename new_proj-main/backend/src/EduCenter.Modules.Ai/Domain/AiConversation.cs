using EduCenter.SharedKernel;

namespace EduCenter.Modules.Ai.Domain;

public enum AiConversationRole
{
    User,
    Assistant
}

/// <summary>One AI Tutor thread (docs/02_database_erd.md §6 AI_CONVERSATION, TZ §32). Kept per student so the tutor can
/// follow up on earlier turns; Writing and Speaking keep their own tables.</summary>
public class AiConversation : TenantEntity
{
    public const int MaxTitleLength = 120;

    private AiConversation() { } // EF Core

    public AiConversation(Guid organizationId, Guid studentId, Guid userId, AiFeature module, string title)
    {
        OrganizationId = organizationId;
        StudentId = studentId;
        UserId = userId;
        Module = module;
        Title = title;
        LastMessageAt = DateTimeOffset.UtcNow;
    }

    public Guid StudentId { get; private set; }
    public Guid UserId { get; private set; }
    public AiFeature Module { get; private set; }
    public string Title { get; private set; } = default!;
    public int MessageCount { get; private set; }
    public DateTimeOffset LastMessageAt { get; private set; }

    public void RecordMessage()
    {
        MessageCount++;
        LastMessageAt = DateTimeOffset.UtcNow;
        MarkUpdated();
    }

    public void Rename(string title)
    {
        Title = title.Length <= MaxTitleLength ? title : title[..MaxTitleLength];
        MarkUpdated();
    }
}

/// <summary>A turn in a conversation. Assistant turns keep the sources they were grounded on and the gateway request id,
/// so an answer can be traced back to its cost and to the material behind it.</summary>
public class AiConversationMessage : TenantEntity
{
    public const int MaxContentLength = 8000;

    private AiConversationMessage() { } // EF Core

    public AiConversationMessage(Guid organizationId, Guid conversationId, AiConversationRole role, string content,
        string? citationsJson = null, Guid? aiRequestId = null)
    {
        OrganizationId = organizationId;
        ConversationId = conversationId;
        Role = role;
        Content = content;
        CitationsJson = citationsJson;
        AiRequestId = aiRequestId;
    }

    public Guid ConversationId { get; private set; }
    public AiConversationRole Role { get; private set; }
    public string Content { get; private set; } = default!;
    public string? CitationsJson { get; private set; }
    public Guid? AiRequestId { get; private set; }
}
