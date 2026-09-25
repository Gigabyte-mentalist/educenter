using System.Collections.Concurrent;
using EduCenter.Api.Auth;
using EduCenter.Infrastructure;
using EduCenter.Modules.Identity.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Chat;

/// <summary>
/// Real-time delivery for chat (docs/00_TZ.md §22). The hub carries no writes: messages, reads and reactions go
/// through <see cref="ChatsController"/> (validated, persisted), which then broadcasts. A connection subscribes to
/// the chats its user can see via <see cref="SyncChats"/> — called after connecting and whenever the chat list
/// changes — so a user who loses access stops receiving on the next sync, and the REST API refuses regardless.
///
/// Server → client events: <c>messageCreated</c>, <c>messageUpdated</c>, <c>readUpdated</c>, <c>typing</c>,
/// <c>chatActivity</c>. Groups are in-memory (single API instance); several instances need a backplane.
/// </summary>
[Authorize]
public class ChatHub(ChatAccess access, AppDbContext db, UserStatusCache statuses) : Hub
{
    private static readonly ConcurrentDictionary<string, HashSet<Guid>> Subscriptions = new();

    /// <summary>userId → its connection ids, so a block can be pushed to every tab/device that user has open —
    /// <see cref="Subscriptions"/> alone only goes from a connection to its chats, not from a user to their connections.</summary>
    private static readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, byte>> UserConnections = new();

    public static string ChatGroup(Guid chatId) => $"chat:{chatId}";
    public static string UserGroup(Guid userId) => $"user:{userId}";

    private Guid UserId => Guid.Parse(Context.UserIdentifier!);

    public override async Task OnConnectedAsync()
    {
        Subscriptions[Context.ConnectionId] = [];
        UserConnections.GetOrAdd(UserId, _ => new()).TryAdd(Context.ConnectionId, 0);
        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(UserId));
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        Subscriptions.TryRemove(Context.ConnectionId, out _);
        if (UserConnections.TryGetValue(UserId, out var connections))
            connections.TryRemove(Context.ConnectionId, out _);
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>Pushed a client cannot ignore: the affected user's connections are told directly, then dropped
    /// from every chat group they were in — a still-open socket keeps working technically, but hears nothing more
    /// and its own calls (<see cref="SyncChats"/>, <see cref="Typing"/>) now refuse.</summary>
    public static async Task DropUserAsync(IHubContext<ChatHub> hub, Guid userId)
    {
        await hub.Clients.Group(UserGroup(userId)).SendAsync("accountBlocked");

        if (!UserConnections.TryGetValue(userId, out var connections)) return;
        foreach (var connectionId in connections.Keys)
        {
            if (!Subscriptions.TryGetValue(connectionId, out var chats)) continue;
            foreach (var chatId in chats)
                await hub.Groups.RemoveFromGroupAsync(connectionId, ChatGroup(chatId));
            Subscriptions[connectionId] = [];
        }
    }

    /// <summary>Subscribes this connection to every chat the user can see now and drops the ones they no longer can.</summary>
    public async Task<int> SyncChats()
    {
        if (await statuses.GetAsync(UserId, Context.ConnectionAborted) is not { Status: UserStatus.Active })
            throw new HubException(ApiErrorCodes.AccountBlocked);

        var visible = (await access.VisibleChatsAsync(UserId, Context.ConnectionAborted)).Select(c => c.Id).ToHashSet();
        var current = Subscriptions.GetOrAdd(Context.ConnectionId, _ => []);

        foreach (var chatId in visible.Except(current).ToList())
            await Groups.AddToGroupAsync(Context.ConnectionId, ChatGroup(chatId));
        foreach (var chatId in current.Except(visible).ToList())
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, ChatGroup(chatId));

        Subscriptions[Context.ConnectionId] = visible;
        return visible.Count;
    }

    public async Task Typing(Guid chatId)
    {
        if (await statuses.GetAsync(UserId, Context.ConnectionAborted) is not { Status: UserStatus.Active })
            throw new HubException(ApiErrorCodes.AccountBlocked);
        if (!Subscriptions.TryGetValue(Context.ConnectionId, out var chats) || !chats.Contains(chatId)) return;

        if (Context.Items["name"] is not string name)
        {
            name = await db.Users.Where(u => u.Id == UserId).Select(u => u.FullName).FirstOrDefaultAsync() ?? "";
            Context.Items["name"] = name;
        }

        await Clients.OthersInGroup(ChatGroup(chatId)).SendAsync("typing", new { chatId, userId = UserId, name });
    }
}
