using System.Text.RegularExpressions;
using EduCenter.SharedKernel;

namespace EduCenter.Modules.Chat.Domain;

public record ChatAttachment(string FileName, string ContentType, long Size, string StorageKey);

public class ChatMessage : TenantEntity
{
    public const int MaxBodyLength = 4000;

    private ChatMessage() { } // EF Core

    /// <summary>
    /// Strips ASCII whitespace, zero-width characters, and invisible Unicode filler/spacer code points
    /// (e.g. Hangul Filler U+3164, Braille Space U+2800, zero-width space U+200B, etc.) so that a message
    /// that looks empty to the reader is treated as empty by the server as well.
    /// </summary>
    public static string? SanitizeBody(string? raw)
    {
        if (raw is null) return null;
        var trimmed = raw.Trim();

        // Strip zero-width / invisible / directional / Braille-space code points.
        trimmed = Regex.Replace(trimmed,
            @"[\u200B-\u200F\u2028\u2029\u202A-\u202E\u2060-\u2064\u206A-\u206F\u2800-\u28FF\u3164\uFEFF\uFFA0]+",
            string.Empty);

        // Collapse 3+ consecutive newlines down to 2 (one blank line max between paragraphs).
        trimmed = Regex.Replace(trimmed, @"\n{3,}", "\n\n");

        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed.Trim();
    }

    public ChatMessage(Guid organizationId, Guid chatId, Guid senderId, string? body, Guid? replyToMessageId, ChatAttachment? attachment)
    {
        OrganizationId = organizationId;
        ChatId = chatId;
        SenderId = senderId;
        Body = SanitizeBody(body);
        ReplyToMessageId = replyToMessageId;
        if (attachment is not null)
        {
            AttachmentName = attachment.FileName;
            AttachmentContentType = attachment.ContentType;
            AttachmentSize = attachment.Size;
            AttachmentKey = attachment.StorageKey;
        }
    }

    public Guid ChatId { get; private set; }
    public Guid SenderId { get; private set; }
    public string? Body { get; private set; }
    public Guid? ReplyToMessageId { get; private set; }

    public string? AttachmentName { get; private set; }
    public string? AttachmentContentType { get; private set; }
    public long? AttachmentSize { get; private set; }
    public string? AttachmentKey { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }
    public Guid? DeletedBy { get; private set; }
    public DateTimeOffset? PinnedAt { get; private set; }
    public Guid? PinnedBy { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    /// <summary>Soft delete: the row stays (replies and read cursors keep pointing at it), content is hidden.</summary>
    public void Delete(Guid by)
    {
        if (IsDeleted) return;
        DeletedAt = DateTimeOffset.UtcNow;
        DeletedBy = by;
        PinnedAt = null;
        PinnedBy = null;
        MarkUpdated();
    }

    public void Pin(Guid by)
    {
        PinnedAt = DateTimeOffset.UtcNow;
        PinnedBy = by;
        MarkUpdated();
    }

    public void Unpin()
    {
        PinnedAt = null;
        PinnedBy = null;
        MarkUpdated();
    }
}

public class ChatReaction
{
    /// <summary>The reaction set offered by the UI; anything else is rejected.</summary>
    public static readonly string[] Allowed = ["👍", "❤️", "😂", "😮", "🙏", "✅"];

    private ChatReaction() { } // EF Core

    public ChatReaction(Guid messageId, Guid userId, string emoji)
    {
        Id = Guid.NewGuid();
        MessageId = messageId;
        UserId = userId;
        Emoji = emoji;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid MessageId { get; private set; }
    public Guid UserId { get; private set; }
    public string Emoji { get; private set; } = default!;
    public DateTimeOffset CreatedAt { get; private set; }
}
