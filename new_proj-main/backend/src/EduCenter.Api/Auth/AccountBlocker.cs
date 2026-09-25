using EduCenter.Api.Chat;
using EduCenter.Modules.Identity.Domain;
using Microsoft.AspNetCore.SignalR;

namespace EduCenter.Api.Auth;

/// <summary>
/// Called right after a user's <see cref="User.Status"/> is saved (activate/deactivate/delete) — evicts the cached
/// status so <see cref="AccountStatusMiddleware"/> sees the change on the very next request instead of waiting out
/// the cache's TTL, and pushes a live chat connection out immediately rather than leaving it to notice on its own.
/// </summary>
public class AccountBlocker(UserStatusCache statuses, IHubContext<ChatHub> chatHub)
{
    public async Task StatusChangedAsync(Guid userId, CancellationToken ct = default)
    {
        statuses.Evict(userId);

        var state = await statuses.GetAsync(userId, ct);
        if (state is not null && state.Status != UserStatus.Active)
            await ChatHub.DropUserAsync(chatHub, userId);
    }
}
