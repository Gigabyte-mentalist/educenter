using System.Security.Claims;
using EduCenter.Infrastructure;
using EduCenter.Modules.Identity.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Auth;

[ApiController]
[Route("api/auth")]
public class AuthController(
    AppDbContext db, JwtTokenService tokenService, AccountBlocker accountBlocker, LoginThrottle loginThrottle, PasswordResetService passwordReset) : ControllerBase
{
    private static readonly PasswordHasher<User> Hasher = new();

    /// <summary>Verified against when the email matches no account, so "no such user" costs as much time as "wrong
    /// password" — otherwise response time tells an outsider which emails have accounts.</summary>
    private static readonly User TimingDecoy = new(Guid.Empty, "decoy", "", "decoy@invalid", string.Empty);
    private static readonly string TimingDecoyHash = Hasher.HashPassword(TimingDecoy, Guid.NewGuid().ToString("N"));

    // Sign-in bodies are a few dozen bytes; anything bigger is not a person typing.
    private const int AuthBodyLimit = 16 * 1024;

    [HttpPost("login")]
    [EnableRateLimiting("LoginBruteForce")]
    [RequestSizeLimit(AuthBodyLimit)]
    public async Task<ActionResult<TokenResponse>> Login(LoginRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (email.Length is 0 or > EmailPolicy.MaxLength)
            return Unauthorized(new { error = "Email yoki parol noto'g'ri." });

        if (loginThrottle.IsLocked(email, out var retryAfter))
        {
            Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                error = "Juda ko'p noto'g'ri urinish. Bir necha daqiqadan so'ng qayta urinib ko'ring.",
            });
        }

        var user = await db.Users
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Email == email);

        if (user is null)
        {
            Hasher.VerifyHashedPassword(TimingDecoy, TimingDecoyHash, request.Password);
            loginThrottle.RecordFailure(email);
            return Unauthorized(new { error = "Email yoki parol noto'g'ri." });
        }

        var verifyResult = Hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verifyResult == PasswordVerificationResult.Failed)
        {
            loginThrottle.RecordFailure(email);
            return Unauthorized(new { error = "Email yoki parol noto'g'ri." });
        }

        loginThrottle.RecordSuccess(email);

        // Only said once the password is confirmed right — otherwise this route would double as "does this
        // email have an account" for anyone who can guess or already knows it.
        if (user.Status != UserStatus.Active)
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = ApiErrorCodes.AccountBlockedMessage,
                code = ApiErrorCodes.AccountBlocked,
            });

        var roleCodes = await db.Roles
            .Where(r => user.Roles.Select(ur => ur.RoleId).Contains(r.Id))
            .Select(r => r.Code)
            .ToListAsync();

        return await IssueTokensAsync(user, roleCodes);
    }

    [HttpPost("refresh")]
    [RequestSizeLimit(AuthBodyLimit)]
    public async Task<ActionResult<TokenResponse>> Refresh(RefreshRequest request)
    {
        var hash = JwtTokenService.Hash(request.RefreshToken);
        var existing = await db.RefreshTokens.FirstOrDefaultAsync(rt => rt.TokenHash == hash);

        if (existing is null)
            return Unauthorized(new { error = "Refresh token yaroqsiz yoki muddati o'tgan." });

        var user = await db.Users
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == existing.UserId);

        if (user is null)
            return Unauthorized(new { error = "Foydalanuvchi topilmadi yoki faol emas." });

        // Checked before IsActive on purpose: blocking a signed-in user revokes their refresh tokens (S01.04) so the
        // client can tell "blocked" from "session expired" instead of both collapsing into the same generic 401.
        if (user.Status != UserStatus.Active)
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = ApiErrorCodes.AccountBlockedMessage,
                code = ApiErrorCodes.AccountBlocked,
            });

        if (!existing.IsActive)
            return Unauthorized(new { error = "Refresh token yaroqsiz yoki muddati o'tgan." });

        existing.Revoke(); // rotation: old refresh token is single-use

        var roleCodes = await db.Roles
            .Where(r => user.Roles.Select(ur => ur.RoleId).Contains(r.Id))
            .Select(r => r.Code)
            .ToListAsync();

        return await IssueTokensAsync(user, roleCodes);
    }

    /// <summary>Self-service password change — the only route <see cref="Modules.Identity.Domain.User.MustChangePassword"/>
    /// stays reachable through while it is set (docs/21.09-audit-jurnali S01.05). All other refresh tokens are revoked
    /// (every other device is signed out), but the caller keeps working: a fresh pair is returned in the same response
    /// so this request's own session does not have to log back in.</summary>
    [HttpPost("change-password")]
    [Authorize]
    [EnableRateLimiting("LoginBruteForce")]
    public async Task<ActionResult<TokenResponse>> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();

        var user = await db.Users.Include(u => u.Roles).FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return Unauthorized();

        // /api/auth/* skips AccountStatusMiddleware, so a blocked account holding a still-valid access token must be
        // refused here — otherwise this route would hand it a brand-new token pair.
        if (user.Status != UserStatus.Active)
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = ApiErrorCodes.AccountBlockedMessage,
                code = ApiErrorCodes.AccountBlocked,
            });

        if (Hasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
            return BadRequest(new { error = "Joriy parol noto'g'ri." });
        if (PasswordPolicy.Validate(request.NewPassword) is { } passwordProblem)
            return BadRequest(new { error = passwordProblem });
        if (Hasher.VerifyHashedPassword(user, user.PasswordHash, request.NewPassword) == PasswordVerificationResult.Success)
            return BadRequest(new { error = "Yangi parol joriy paroldan farq qilsin." });

        user.ChangePassword(Hasher.HashPassword(user, request.NewPassword));

        // Every other device's session ends — a temporary or leaked password may have been used elsewhere.
        await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTimeOffset.UtcNow), ct);

        var roleCodes = await db.Roles
            .Where(r => user.Roles.Select(ur => ur.RoleId).Contains(r.Id))
            .Select(r => r.Code)
            .ToListAsync(ct);

        var response = await IssueTokensAsync(user, roleCodes);
        // The stale MustChangePassword=true would otherwise sit in the cache for up to 30s, blocking the very
        // request that just fixed it.
        await accountBlocker.StatusChangedAsync(userId, ct);
        return response;
    }

    /// <summary>"Parolni unutdim", step one: sends a six-digit code to the account's email (or, for a phone number, by SMS). It answers 204 for anyone —
    /// an unknown address, a blocked account, a throttled one, a channel nobody configured — so the reply reveals nothing about who has an account.</summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting("LoginBruteForce")]
    [RequestSizeLimit(AuthBodyLimit)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken ct)
    {
        await passwordReset.RequestAsync(request.Identifier, ct);
        return NoContent();
    }

    /// <summary>Step two: the code and a new password. Every failure to identify the person — no such account, blocked, wrong or spent or expired
    /// code — is the same 400, so it cannot be used to find out which of them it was.</summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting("LoginBruteForce")]
    [RequestSizeLimit(AuthBodyLimit)]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        var result = await passwordReset.ResetAsync(request.Identifier, request.Code, request.NewPassword, ct);
        return result.Success ? NoContent() : BadRequest(new { error = result.Error });
    }

    [HttpPost("logout")]
    [RequestSizeLimit(AuthBodyLimit)]
    public async Task<IActionResult> Logout(LogoutRequest request)
    {
        var hash = JwtTokenService.Hash(request.RefreshToken);
        var existing = await db.RefreshTokens.FirstOrDefaultAsync(rt => rt.TokenHash == hash);

        if (existing is not null && existing.IsActive)
        {
            existing.Revoke();
            await db.SaveChangesAsync();
        }

        return NoContent();
    }

    private async Task<ActionResult<TokenResponse>> IssueTokensAsync(User user, List<string> roleCodes)
    {
        var access = tokenService.CreateAccessToken(user, roleCodes);
        var refresh = tokenService.CreateRefreshToken();

        db.RefreshTokens.Add(new RefreshToken(user.Id, refresh.Hash, refresh.ExpiresAt));
        await db.SaveChangesAsync();

        return Ok(new TokenResponse(access.Value, access.ExpiresAt, refresh.RawValue, user.MustChangePassword));
    }
}
