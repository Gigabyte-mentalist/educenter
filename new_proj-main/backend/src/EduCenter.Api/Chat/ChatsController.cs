using System.Security.Claims;
using EduCenter.Api.Authorization;
using EduCenter.Infrastructure;
using EduCenter.Modules.Chat.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Chat;

public record ChatLastMessageDto(Guid SenderId, string SenderName, string Preview, DateTimeOffset CreatedAt);

public record ChatListItemDto(
    Guid Id, ChatType Type, string Title, string? Subtitle, Guid? GroupId, Guid? BranchId, StaffChannelKind? ChannelKind,
    ChatContact? Counterpart, ChatLastMessageDto? LastMessage, int UnreadCount, bool CanPost, DateTimeOffset? LastMessageAt);

public record ChatReaderDto(Guid UserId, string FullName, DateTimeOffset LastReadAt);
public record ChatReplyDto(Guid Id, Guid SenderId, string SenderName, string Preview);
public record ChatAttachmentDto(string FileName, string ContentType, long Size, bool IsImage);
public record ChatReactionDto(string Emoji, int Count, List<Guid> UserIds);

public record ChatMessageDto(
    Guid Id, Guid ChatId, Guid SenderId, string SenderName, string? SenderRole, string? Body, DateTimeOffset CreatedAt,
    bool IsDeleted, ChatReplyDto? ReplyTo, ChatAttachmentDto? Attachment, List<ChatReactionDto> Reactions, DateTimeOffset? PinnedAt);

public record ChatDetailDto(ChatListItemDto Chat, bool CanModerate, bool CanPin, bool CanDelete, List<ChatReaderDto> Readers, List<ChatMessageDto> Pinned);

public record CreateDirectChatRequest(Guid UserId);
public record ToggleReactionRequest(string Emoji);

public class SendMessageForm
{
    public string? Body { get; set; }
    public Guid? ReplyToMessageId { get; set; }
    public IFormFile? File { get; set; }
}

/// <summary>Chat REST API (docs/00_TZ.md §22, matrix §3.8). Every write is persisted here and then broadcast through
/// <see cref="ChatHub"/>. A chat the caller may not read answers 404, so its existence is not disclosed.</summary>
[ApiController]
[Route("api/chats")]
[Authorize]
public class ChatsController(AppDbContext db, ChatAccess access, ChatFileStorage files, IHubContext<ChatHub> hub,
    PermissionResolver permissions) : ControllerBase
{
    private const int PreviewLength = 120;

    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<ActionResult<List<ChatListItemDto>>> List(CancellationToken ct)
    {
        var chats = await access.VisibleChatsAsync(UserId, ct);
        var items = await BuildItemsAsync(chats, ct);
        return Ok(items
            .OrderByDescending(i => i.LastMessageAt.HasValue)
            .ThenByDescending(i => i.LastMessageAt)
            .ThenBy(i => i.Type)
            .ThenBy(i => i.Title)
            .ToList());
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<object>> UnreadCount(CancellationToken ct)
    {
        var chatIds = (await access.VisibleChatsAsync(UserId, ct)).Select(c => c.Id).ToList();
        var counts = await UnreadCountsAsync(chatIds, ct);
        return Ok(new { count = counts.Values.Sum() });
    }

    [HttpGet("contacts")]
    [Authorize(Policy = ChatPermissions.Direct)]
    public async Task<ActionResult<List<ChatContact>>> Contacts([FromQuery] string? search, CancellationToken ct) =>
        Ok(await access.ContactsAsync(HttpContext.RequireResolvedScope(), search, ct));

    /// <summary>Opens the direct chat with a contact, creating it on first use (one chat per pair of users).</summary>
    [HttpPost("direct")]
    [Authorize(Policy = ChatPermissions.Direct)]
    public async Task<ActionResult<ChatListItemDto>> OpenDirect(CreateDirectChatRequest request, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (request.UserId == scope.UserId) return BadRequest(new { error = "O'zingiz bilan suhbat ochib bo'lmaydi." });
        if (!(await access.ContactIdsAsync(scope, ct)).Contains(request.UserId))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Bu foydalanuvchiga xabar yozish uchun ruxsat yo'q." });

        var key = ChatRoom.DirectKeyFor(scope.UserId, request.UserId);
        var chat = await db.ChatRooms.FirstOrDefaultAsync(c => c.OrganizationId == scope.OrganizationId && c.DirectKey == key, ct);
        if (chat is null)
        {
            chat = ChatRoom.Direct(scope.OrganizationId, scope.UserId, request.UserId);
            db.ChatRooms.Add(chat);
            db.ChatMembers.AddRange(new ChatMember(chat.Id, scope.UserId), new ChatMember(chat.Id, request.UserId));
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
                chat = await db.ChatRooms.FirstAsync(c => c.OrganizationId == scope.OrganizationId && c.DirectKey == key, ct);
            }
        }

        return Ok((await BuildItemsAsync([chat], ct)).Single());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ChatDetailDto>> Get(Guid id, CancellationToken ct)
    {
        var (chat, caps) = await LoadAsync(id, ct);
        if (chat is null || !caps.CanRead) return NotFound();

        var readers = await (
            from m in db.ChatMembers
            join u in db.Users on m.UserId equals u.Id
            where m.ChatId == id && m.LastReadAt != null
            orderby m.LastReadAt descending
            select new ChatReaderDto(u.Id, u.FullName, m.LastReadAt!.Value)).Take(200).ToListAsync(ct);

        var pinned = await db.ChatMessages
            .Where(m => m.ChatId == id && m.PinnedAt != null && m.DeletedAt == null)
            .OrderByDescending(m => m.PinnedAt).Take(20).ToListAsync(ct);

        // Resolved here rather than inferred from the role in the token: the delete button must appear for
        // exactly the people the policy would let through, and a direct chat is deletable while a group chat
        // backed by a live group is not — so the answer is per chat, not per user.
        var canDelete = await permissions.ResolveAsync(UserId, ChatPermissions.Delete, ct) is not null
            && chat.Type == ChatType.Direct;

        return Ok(new ChatDetailDto((await BuildItemsAsync([chat], ct)).Single(), caps.CanModerate, CanPin(chat, caps),
            canDelete, readers, await ProjectAsync(pinned, ct)));
    }

    [HttpGet("{id:guid}/messages")]
    public async Task<ActionResult<List<ChatMessageDto>>> Messages(Guid id, [FromQuery] DateTimeOffset? before,
        [FromQuery] int limit = 50, [FromQuery] string? search = null, CancellationToken ct = default)
    {
        var (chat, caps) = await LoadAsync(id, ct);
        if (chat is null || !caps.CanRead) return NotFound();

        var query = db.ChatMessages.Where(m => m.ChatId == id);
        if (before is not null) query = query.Where(m => m.CreatedAt < before);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(m => m.DeletedAt == null && m.Body != null && EF.Functions.ILike(m.Body, $"%{search.Trim()}%"));

        var page = await query.OrderByDescending(m => m.CreatedAt).Take(Math.Clamp(limit, 1, 100)).ToListAsync(ct);
        page.Reverse();
        return Ok(await ProjectAsync(page, ct));
    }

    [HttpPost("{id:guid}/messages")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 11 * 1024 * 1024)]
    public async Task<ActionResult<ChatMessageDto>> Send(Guid id, [FromForm] SendMessageForm form, CancellationToken ct)
    {
        var (chat, caps) = await LoadAsync(id, ct);
        if (chat is null || !caps.CanRead) return NotFound();
        if (!caps.CanPost) return StatusCode(StatusCodes.Status403Forbidden, new { error = "Bu kanalga faqat administratsiya yozadi." });

        var body = ChatMessage.SanitizeBody(form.Body);
        if (body is null && form.File is null) return BadRequest(new { error = "Xabar matni yoki fayl kerak." });
        if (body is { Length: > ChatMessage.MaxBodyLength })
            return BadRequest(new { error = $"Xabar {ChatMessage.MaxBodyLength} belgidan oshmasligi kerak." });
        if (form.ReplyToMessageId is { } replyId && !await db.ChatMessages.AnyAsync(m => m.Id == replyId && m.ChatId == id, ct))
            return BadRequest(new { error = "Javob berilayotgan xabar bu suhbatda topilmadi." });

        ChatAttachment? attachment = null;
        if (form.File is not null)
        {
            var problem = await files.ValidateAsync(form.File, ct);
            if (problem is not null) return BadRequest(new { error = problem });
            attachment = await files.SaveAsync(form.File, ct);
        }

        var message = new ChatMessage(chat.OrganizationId, id, UserId, body, form.ReplyToMessageId, attachment);
        db.ChatMessages.Add(message);
        chat.Touch(message.CreatedAt);
        (await MemberAsync(id, ct)).MarkRead(message.CreatedAt);
        await db.SaveChangesAsync(ct);

        var dto = (await ProjectAsync([message], ct)).Single();
        await hub.Clients.Group(ChatHub.ChatGroup(id)).SendAsync("messageCreated", dto, ct);
        if (chat.Type == ChatType.Direct)
        {
            // The other participant may not be subscribed yet (a chat opened with them a moment ago).
            var members = await db.ChatMembers.Where(m => m.ChatId == id).Select(m => m.UserId).ToListAsync(ct);
            await hub.Clients.Groups(members.Select(ChatHub.UserGroup).ToList()).SendAsync("chatActivity", new { chatId = id }, ct);
        }

        return Created($"/api/chats/{id}/messages/{message.Id}", dto);
    }

    /// <summary>The author deletes their own message; a moderator deletes anyone's (matrix: chat.moderate).</summary>
    [HttpDelete("{id:guid}/messages/{messageId:guid}")]
    public async Task<ActionResult<ChatMessageDto>> Delete(Guid id, Guid messageId, CancellationToken ct)
    {
        var (chat, caps) = await LoadAsync(id, ct);
        if (chat is null || !caps.CanRead) return NotFound();
        var message = await db.ChatMessages.FirstOrDefaultAsync(m => m.Id == messageId && m.ChatId == id, ct);
        if (message is null) return NotFound();
        if (message.SenderId != UserId && !caps.CanModerate)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Faqat o'z xabaringizni o'chira olasiz." });

        message.Delete(UserId);
        await db.SaveChangesAsync(ct);
        return await BroadcastUpdatedAsync(message, ct);
    }

    /// <summary>
    /// Deletes a whole conversation with everything said in it, for an administrator clearing out the chat of
    /// someone who has left. Group chats and staff channels are excluded while their group or branch still
    /// exists: those are generated from the group, so deleting one only makes the app recreate it on the next
    /// message, and the deletion would read as a bug rather than a no-op.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = ChatPermissions.Delete)]
    public async Task<IActionResult> DeleteChat(Guid id, CancellationToken ct)
    {
        var (chat, caps) = await LoadAsync(id, ct);
        if (chat is null || !caps.CanRead) return NotFound();

        if (chat.Type != ChatType.Direct)
        {
            var stillBacked = chat.GroupId is { } groupId
                ? await db.Groups.AnyAsync(g => g.Id == groupId, ct)
                : chat.BranchId is { } branchId && await db.Branches.AnyAsync(b => b.Id == branchId, ct);

            if (stillBacked)
                return Conflict(new
                {
                    error = "Guruh chati va xodimlar kanalini alohida o'chirib bo'lmaydi — u guruh (yoki filial) "
                        + "bilan birga yaratiladi. Guruh o'chirilsa, chat ham o'chadi.",
                });
        }

        // Attachments live on disk, not in the database, so they have to be removed by hand — otherwise the
        // rows go and the files stay behind forever, taking up space nothing can ever reach again.
        var attachmentKeys = await db.ChatMessages
            .Where(m => m.ChatId == id && m.AttachmentKey != null)
            .Select(m => m.AttachmentKey!)
            .ToListAsync(ct);

        var messageIds = await db.ChatMessages.Where(m => m.ChatId == id).Select(m => m.Id).ToListAsync(ct);
        await db.ChatReactions.Where(r => messageIds.Contains(r.MessageId)).ExecuteDeleteAsync(ct);
        await db.ChatMessages.Where(m => m.ChatId == id).ExecuteDeleteAsync(ct);

        var members = await db.ChatMembers.Where(m => m.ChatId == id).Select(m => m.UserId).ToListAsync(ct);
        await db.ChatMembers.Where(m => m.ChatId == id).ExecuteDeleteAsync(ct);

        db.ChatRooms.Remove(chat);
        await db.SaveChangesAsync(ct);

        foreach (var key in attachmentKeys) files.Delete(key);

        // Everyone who had it open gets it removed from their list instead of a chat that 404s on every poll.
        await hub.Clients.Groups(members.Select(ChatHub.UserGroup).ToList())
            .SendAsync("chatDeleted", new { chatId = id }, ct);

        return NoContent();
    }

    [HttpPost("{id:guid}/messages/{messageId:guid}/pin")]
    public Task<ActionResult<ChatMessageDto>> Pin(Guid id, Guid messageId, CancellationToken ct) => SetPinnedAsync(id, messageId, true, ct);

    [HttpDelete("{id:guid}/messages/{messageId:guid}/pin")]
    public Task<ActionResult<ChatMessageDto>> Unpin(Guid id, Guid messageId, CancellationToken ct) => SetPinnedAsync(id, messageId, false, ct);

    [HttpPost("{id:guid}/messages/{messageId:guid}/reactions")]
    public async Task<ActionResult<ChatMessageDto>> ToggleReaction(Guid id, Guid messageId, ToggleReactionRequest request, CancellationToken ct)
    {
        var (chat, caps) = await LoadAsync(id, ct);
        if (chat is null || !caps.CanRead) return NotFound();
        if (!ChatReaction.Allowed.Contains(request.Emoji)) return BadRequest(new { error = "Bunday reaksiya yo'q." });
        var message = await db.ChatMessages.FirstOrDefaultAsync(m => m.Id == messageId && m.ChatId == id && m.DeletedAt == null, ct);
        if (message is null) return NotFound();

        var existing = await db.ChatReactions.FirstOrDefaultAsync(r => r.MessageId == messageId && r.UserId == UserId && r.Emoji == request.Emoji, ct);
        if (existing is null) db.ChatReactions.Add(new ChatReaction(messageId, UserId, request.Emoji));
        else db.ChatReactions.Remove(existing);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear(); // a double click raced itself; the reaction is already there
        }

        return await BroadcastUpdatedAsync(message, ct);
    }

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        var (chat, caps) = await LoadAsync(id, ct);
        if (chat is null || !caps.CanRead) return NotFound();

        var member = await MemberAsync(id, ct);
        member.MarkRead(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);

        await hub.Clients.Group(ChatHub.ChatGroup(id)).SendAsync("readUpdated", new { chatId = id, userId = UserId, lastReadAt = member.LastReadAt }, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/messages/{messageId:guid}/attachment")]
    public async Task<IActionResult> Attachment(Guid id, Guid messageId, CancellationToken ct)
    {
        var (chat, caps) = await LoadAsync(id, ct);
        if (chat is null || !caps.CanRead) return NotFound();

        var message = await db.ChatMessages.FirstOrDefaultAsync(m => m.Id == messageId && m.ChatId == id && m.DeletedAt == null, ct);
        if (message?.AttachmentKey is null) return NotFound();
        var path = files.PathFor(message.AttachmentKey);
        if (path is null) return NotFound();

        // Images render inline in the conversation; everything else downloads.
        return ChatFileStorage.IsImage(message.AttachmentContentType!)
            ? PhysicalFile(path, message.AttachmentContentType!)
            : PhysicalFile(path, message.AttachmentContentType!, message.AttachmentName);
    }

    // ---- helpers -------------------------------------------------------------------------------

    private async Task<(ChatRoom? Chat, ChatCapabilities Caps)> LoadAsync(Guid id, CancellationToken ct)
    {
        var chat = await db.ChatRooms.FirstOrDefaultAsync(c => c.Id == id, ct);
        return chat is null ? (null, ChatCapabilities.None) : (chat, await access.CapabilitiesAsync(UserId, chat, ct));
    }

    /// <summary>Direct chats: either participant pins. Group chats and channels: moderators.</summary>
    private static bool CanPin(ChatRoom chat, ChatCapabilities caps) => chat.Type == ChatType.Direct ? caps.CanRead : caps.CanModerate;

    private async Task<ActionResult<ChatMessageDto>> SetPinnedAsync(Guid id, Guid messageId, bool pinned, CancellationToken ct)
    {
        var (chat, caps) = await LoadAsync(id, ct);
        if (chat is null || !caps.CanRead) return NotFound();
        if (!CanPin(chat, caps)) return StatusCode(StatusCodes.Status403Forbidden, new { error = "Xabarni faqat moderator qadaydi." });

        var message = await db.ChatMessages.FirstOrDefaultAsync(m => m.Id == messageId && m.ChatId == id && m.DeletedAt == null, ct);
        if (message is null) return NotFound();

        if (pinned) message.Pin(UserId);
        else message.Unpin();
        await db.SaveChangesAsync(ct);
        return await BroadcastUpdatedAsync(message, ct);
    }

    private async Task<ActionResult<ChatMessageDto>> BroadcastUpdatedAsync(ChatMessage message, CancellationToken ct)
    {
        var dto = (await ProjectAsync([message], ct)).Single();
        await hub.Clients.Group(ChatHub.ChatGroup(message.ChatId)).SendAsync("messageUpdated", dto, ct);
        return Ok(dto);
    }

    private async Task<ChatMember> MemberAsync(Guid chatId, CancellationToken ct)
    {
        var member = await db.ChatMembers.FirstOrDefaultAsync(m => m.ChatId == chatId && m.UserId == UserId, ct);
        if (member is not null) return member;

        member = new ChatMember(chatId, UserId);
        db.ChatMembers.Add(member);
        return member;
    }

    private async Task<Dictionary<Guid, int>> UnreadCountsAsync(List<Guid> chatIds, CancellationToken ct)
    {
        var me = UserId;
        return await (
            from m in db.ChatMessages
            where chatIds.Contains(m.ChatId) && m.SenderId != me && m.DeletedAt == null
                && !db.ChatMembers.Any(cm => cm.ChatId == m.ChatId && cm.UserId == me && cm.LastReadAt != null && cm.LastReadAt >= m.CreatedAt)
            group m by m.ChatId into g
            select new { g.Key, Count = g.Count() }
        ).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
    }

    private async Task<List<ChatListItemDto>> BuildItemsAsync(List<ChatRoom> chats, CancellationToken ct)
    {
        var me = UserId;
        var chatIds = chats.Select(c => c.Id).ToList();

        var groupIds = chats.Where(c => c.GroupId != null).Select(c => c.GroupId!.Value).ToList();
        var groupNames = await db.Groups.Where(g => groupIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.Name, ct);
        var branchIds = chats.Where(c => c.BranchId != null).Select(c => c.BranchId!.Value).Distinct().ToList();
        var branchNames = await db.Branches.Where(b => branchIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Name, ct);

        var counterparts = await (
            from m in db.ChatMembers
            join u in db.Users on m.UserId equals u.Id
            where chatIds.Contains(m.ChatId) && m.UserId != me
            select new { m.ChatId, u.Id, u.FullName }).ToListAsync(ct);
        var counterpartRoles = await access.RoleCodesAsync(counterparts.Select(c => c.Id).Distinct().ToList(), ct);

        var lastMessages = await (
            from m in db.ChatMessages
            join c in db.ChatRooms on m.ChatId equals c.Id
            where chatIds.Contains(c.Id) && m.CreatedAt == c.LastMessageAt
            select m).ToListAsync(ct);
        var senderIds = lastMessages.Select(m => m.SenderId).Distinct().ToList();
        var senderNames = await db.Users.Where(u => senderIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        var unread = await UnreadCountsAsync(chatIds, ct);

        // Only announcement channels restrict posting beyond read access, and that depends on branch moderation alone.
        var announcementCaps = new Dictionary<Guid, bool>();
        foreach (var channel in chats.Where(c => c.ChannelKind == StaffChannelKind.Announcements))
            announcementCaps[channel.Id] = (await access.CapabilitiesAsync(me, channel, ct)).CanPost;

        return chats.Select(chat =>
        {
            var counterpart = chat.Type == ChatType.Direct
                ? counterparts.Where(c => c.ChatId == chat.Id).Select(c => new ChatContact(c.Id, c.FullName, counterpartRoles.GetValueOrDefault(c.Id))).FirstOrDefault()
                : null;
            var branchName = chat.BranchId is { } b ? branchNames.GetValueOrDefault(b) : null;
            var title = chat.Type switch
            {
                ChatType.Group => groupNames.GetValueOrDefault(chat.GroupId!.Value) ?? "Guruh",
                ChatType.StaffChannel => StaffChannelCatalog.Get(chat.ChannelKind!.Value).Title,
                _ => counterpart?.FullName ?? "Suhbat"
            };
            var last = lastMessages.FirstOrDefault(m => m.ChatId == chat.Id);

            return new ChatListItemDto(chat.Id, chat.Type, title, chat.Type == ChatType.Direct ? null : branchName,
                chat.GroupId, chat.BranchId, chat.ChannelKind, counterpart,
                last is null ? null : new ChatLastMessageDto(last.SenderId, senderNames.GetValueOrDefault(last.SenderId) ?? "", Preview(last), last.CreatedAt),
                unread.GetValueOrDefault(chat.Id), announcementCaps.GetValueOrDefault(chat.Id, true), chat.LastMessageAt);
        }).ToList();
    }

    private async Task<List<ChatMessageDto>> ProjectAsync(List<ChatMessage> messages, CancellationToken ct)
    {
        if (messages.Count == 0) return [];
        var ids = messages.Select(m => m.Id).ToList();

        var replyIds = messages.Where(m => m.ReplyToMessageId != null).Select(m => m.ReplyToMessageId!.Value).Distinct().ToList();
        var replies = await db.ChatMessages.Where(m => replyIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, ct);

        var userIds = messages.Select(m => m.SenderId).Concat(replies.Values.Select(r => r.SenderId)).Distinct().ToList();
        var names = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var roles = await access.RoleCodesAsync(userIds, ct);

        var reactions = (await db.ChatReactions.Where(r => ids.Contains(r.MessageId)).OrderBy(r => r.CreatedAt).ToListAsync(ct))
            .ToLookup(r => r.MessageId);

        return messages.Select(m =>
        {
            ChatReplyDto? reply = null;
            if (m.ReplyToMessageId is { } replyId && replies.TryGetValue(replyId, out var original))
                reply = new ChatReplyDto(original.Id, original.SenderId, names.GetValueOrDefault(original.SenderId) ?? "", Preview(original));

            var attachment = m.AttachmentKey is null || m.IsDeleted
                ? null
                : new ChatAttachmentDto(m.AttachmentName!, m.AttachmentContentType!, m.AttachmentSize ?? 0, ChatFileStorage.IsImage(m.AttachmentContentType!));

            var messageReactions = m.IsDeleted
                ? []
                : reactions[m.Id].GroupBy(r => r.Emoji).Select(g => new ChatReactionDto(g.Key, g.Count(), g.Select(r => r.UserId).ToList())).ToList();

            return new ChatMessageDto(m.Id, m.ChatId, m.SenderId, names.GetValueOrDefault(m.SenderId) ?? "", roles.GetValueOrDefault(m.SenderId),
                m.IsDeleted ? null : m.Body, m.CreatedAt, m.IsDeleted, reply, attachment, messageReactions, m.PinnedAt);
        }).ToList();
    }

    private static string Preview(ChatMessage message)
    {
        if (message.IsDeleted) return "Xabar o'chirildi";
        if (!string.IsNullOrEmpty(message.Body))
            return message.Body.Length <= PreviewLength ? message.Body : message.Body[..PreviewLength] + "…";
        return message.AttachmentContentType is { } type && ChatFileStorage.IsImage(type) ? "Rasm" : $"Fayl: {message.AttachmentName}";
    }
}
