using System.Security.Cryptography;
using System.Text.RegularExpressions;
using EduCenter.Api.Automation;
using EduCenter.Api.Notifications.Delivery;
using EduCenter.Infrastructure;
using EduCenter.Modules.Audit.Domain;
using EduCenter.Modules.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NotificationChannel = EduCenter.Modules.Notifications.Domain.NotificationChannel;

namespace EduCenter.Api.Auth;

public sealed record ResetResult(bool Success, string? Error)
{
    public static ResetResult Done { get; } = new(true, null);
    public static ResetResult Failed(string error) => new(false, error);
}

/// <summary>
/// "Parolni unutdim" (docs/00_TZ.md §49, docs/21.09-audit-jurnali S06.04): a six-digit code sent to the person's email (or, when they typed a phone
/// number, by SMS), good for 15 minutes and five guesses, and then a new password. Nothing here says whether an account exists — asking for a
/// code always looks the same, and every way a reset can fail reads the same — and nothing here writes the code anywhere but the message to the person.
/// </summary>
public class PasswordResetService(
    AppDbContext db, IEnumerable<INotificationSender> senders, IOptions<AutomationOptions> automation, AccountBlocker accountBlocker,
    LoginThrottle loginThrottle, TimeProvider clock, ILogger<PasswordResetService> logger)
{
    /// <summary>More requests than this in an hour for one account are ignored (silently: an answer would reveal the account).</summary>
    public const int MaxRequestsPerHour = 3;

    public const string InvalidCodeMessage = "Kod noto'g'ri yoki muddati o'tgan.";

    // The hasher needs a "user" only as a namespace for its API; the salt and the iterations are in the hash itself.
    private static readonly PasswordHasher<PasswordResetToken> CodeHasher = new();
    private static readonly PasswordResetToken Decoy = new(Guid.Empty, "decoy");
    private static readonly string DecoyHash = CodeHasher.HashPassword(Decoy, "000000");
    private static readonly PasswordHasher<User> PasswordHasher = new();

    /// <summary>Sends a code if the identifier belongs to exactly one active account. Says nothing either way.</summary>
    public async Task RequestAsync(string? identifier, CancellationToken ct)
    {
        var channel = ChannelFor(identifier);
        var sender = senders.FirstOrDefault(s => s.Channel == channel && s.IsEnabled);
        if (sender is null)
        {
            // Nobody can be told a code, whoever asks — so this is a deployment problem, worth a line in the log.
            logger.LogWarning("A password reset was asked for, but the {Channel} channel is not configured.", channel);
            return;
        }

        var user = await FindAsync(identifier, ct);
        if (user is null || user.Status != UserStatus.Active)
        {
            CodeHasher.HashPassword(Decoy, NewCode()); // the same work as the real path, so the time taken does not tell
            return;
        }

        var since = clock.GetUtcNow().AddHours(-1);
        if (await db.PasswordResetTokens.CountAsync(t => t.UserId == user.Id && t.CreatedAt > since, ct) >= MaxRequestsPerHour)
        {
            CodeHasher.HashPassword(Decoy, NewCode());
            return;
        }

        // A new code replaces the old one — only the latest works.
        foreach (var earlier in await db.PasswordResetTokens.Where(t => t.UserId == user.Id && t.UsedAt == null).ToListAsync(ct))
            earlier.Supersede();

        var code = NewCode();
        db.PasswordResetTokens.Add(new PasswordResetToken(user.Id, CodeHasher.HashPassword(Decoy, code)));
        await db.SaveChangesAsync(ct);

        var target = new NotificationTarget(user.Id, user.FullName, user.Email, user.Phone);
        var message = new NotificationEnvelope("Tasdiqlash kodi",
            $"EduCenter: parolni tiklash kodi {code}. U {(int)PasswordResetToken.Lifetime.TotalMinutes} daqiqa amal qiladi. Kodni hech kimga aytmang.");

        // Away from the request where there is a background to hand it to: a slow mail server must not make "this account exists" measurable.
        if (automation.Value.Enabled) _ = Task.Run(() => SendQuietlyAsync(sender, target, message));
        else await SendQuietlyAsync(sender, target, message);
    }

    /// <summary>Checks the code and sets the new password. Any failure to identify the person reads as <see cref="InvalidCodeMessage"/>.</summary>
    public async Task<ResetResult> ResetAsync(string? identifier, string? code, string? newPassword, CancellationToken ct)
    {
        if (PasswordPolicy.Validate(newPassword) is { } weak) return ResetResult.Failed(weak);

        var user = await FindAsync(identifier, ct);
        var token = user is null || user.Status != UserStatus.Active
            ? null
            : await db.PasswordResetTokens.Where(t => t.UserId == user.Id && t.UsedAt == null)
                .OrderByDescending(t => t.CreatedAt).FirstOrDefaultAsync(ct);

        var typed = Regex.Replace(code ?? "", @"\D", "");
        if (user is null || token is null || !token.IsUsable)
        {
            CodeHasher.VerifyHashedPassword(Decoy, DecoyHash, typed);
            return ResetResult.Failed(InvalidCodeMessage);
        }

        if (CodeHasher.VerifyHashedPassword(Decoy, token.CodeHash, typed) == PasswordVerificationResult.Failed)
        {
            token.RecordWrongGuess();   // the fifth wrong guess kills the code
            await db.SaveChangesAsync(ct);
            return ResetResult.Failed(InvalidCodeMessage);
        }

        token.Consume();
        user.ChangePassword(PasswordHasher.HashPassword(user, newPassword!));   // also clears a temporary password's "must change" flag

        // Whoever held the old password — or the old session — is signed out everywhere.
        await db.RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTimeOffset.UtcNow), ct);

        // The request is anonymous, so the automatic audit (which needs a signed-in actor) has nobody to name: the person is the actor.
        db.AuditLogs.Add(new AuditLog(user.OrganizationId, user.Id, "PasswordReset", "User", user.Id, null, null, "Parol bir martalik kod orqali tiklandi."));
        await db.SaveChangesAsync(ct);

        loginThrottle.RecordSuccess(user.Email);   // a lock somebody else's wrong guesses put on the account ends with it
        await accountBlocker.StatusChangedAsync(user.Id, ct);
        return ResetResult.Done;
    }

    /// <summary>An email address when it has an "@"; otherwise a phone number, which counts only if it belongs to one account (phones are not unique).</summary>
    private async Task<User?> FindAsync(string? identifier, CancellationToken ct)
    {
        var text = identifier?.Trim() ?? "";
        if (text.Length is 0 or > EmailPolicy.MaxLength) return null;

        if (text.Contains('@'))
        {
            var email = text.ToLowerInvariant();
            return await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        }

        var digits = Regex.Replace(text, @"\D", "");
        if (digits.Length < 9) return null;
        var last = digits[^9..];
        var matches = await db.Users
            .Where(u => u.Phone.Replace(" ", "").Replace("-", "").Replace("+", "").Replace("(", "").Replace(")", "").EndsWith(last))
            .Take(2)
            .ToListAsync(ct);
        return matches.Count == 1 ? matches[0] : null;
    }

    private static NotificationChannel ChannelFor(string? identifier) =>
        identifier?.Contains('@') == true ? NotificationChannel.Email : NotificationChannel.Sms;

    private static string NewCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    private async Task SendQuietlyAsync(INotificationSender sender, NotificationTarget target, NotificationEnvelope message)
    {
        try
        {
            var result = await sender.SendAsync(target, message, CancellationToken.None);
            if (!result.Success) logger.LogWarning("A password reset code was not sent on {Channel}: {Reason}", sender.Channel, result.Error);
        }
        catch (Exception ex)
        {
            // The message (and the code in it) is deliberately not part of the log.
            logger.LogWarning(ex, "A password reset code could not be sent on {Channel}.", sender.Channel);
        }
    }
}
