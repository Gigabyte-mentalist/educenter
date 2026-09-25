using System.Security.Claims;
using EduCenter.Modules.Identity.Domain;

namespace EduCenter.Api.Auth;

/// <summary>
/// Turns a blocked account into an immediate 403 on every authenticated request, instead of only at the next
/// login — before this, a signed-in user kept working normally until their access token happened to expire.
/// Also enforces a pending forced password change (S01.05) the same way: a temporary password (fresh
/// registration, or just reset by an administrator) is good for nothing but <c>change-password</c> until replaced.
/// Sits after <c>UseAuthentication</c> (so <c>HttpContext.User</c> is populated) and before <c>UseRateLimiter</c>.
/// <c>/api/auth/*</c> is exempt: <c>login</c>, <c>refresh</c> and <c>change-password</c> check state themselves
/// (with the right message for each), and <c>logout</c> must work even for a blocked account so its token gets revoked.
/// </summary>
public class AccountStatusMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, UserStatusCache statuses)
    {
        var isAuthenticated = context.User.Identity?.IsAuthenticated == true;
        var isAuthPath = context.Request.Path.StartsWithSegments("/api/auth");

        if (!isAuthenticated || isAuthPath)
        {
            await next(context);
            return;
        }

        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            await next(context);
            return;
        }

        var state = await statuses.GetAsync(userId, context.RequestAborted);
        if (state is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Foydalanuvchi topilmadi." });
            return;
        }

        if (state.Status != UserStatus.Active)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                error = ApiErrorCodes.AccountBlockedMessage,
                code = ApiErrorCodes.AccountBlocked,
            });
            return;
        }

        if (state.MustChangePassword)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Avval vaqtinchalik parolni o'zgartiring.",
                code = ApiErrorCodes.PasswordChangeRequired,
            });
            return;
        }

        await next(context);
    }
}
