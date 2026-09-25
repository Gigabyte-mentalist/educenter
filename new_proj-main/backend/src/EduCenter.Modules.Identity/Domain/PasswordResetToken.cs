namespace EduCenter.Modules.Identity.Domain;

/// <summary>
/// One "I forgot my password" code (docs/21.09-audit-jurnali S06.04). Only a salted hash of the six digits is stored — a database
/// leak must not hand out working codes — and a code is good for a few minutes and a few guesses, once.
/// </summary>
public class PasswordResetToken
{
    public const int MaxAttempts = 5;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private PasswordResetToken() { } // EF Core

    public PasswordResetToken(Guid userId, string codeHash)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        CodeHash = codeHash;
        CreatedAt = DateTimeOffset.UtcNow;
        ExpiresAt = CreatedAt + Lifetime;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string CodeHash { get; private set; } = default!;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Set when the code was used, or replaced by a newer one.</summary>
    public DateTimeOffset? UsedAt { get; private set; }

    /// <summary>Wrong guesses so far; at <see cref="MaxAttempts"/> the code is dead even if the next guess would have been right.</summary>
    public int Attempts { get; private set; }

    public bool IsUsable => UsedAt is null && ExpiresAt > DateTimeOffset.UtcNow && Attempts < MaxAttempts;

    public void RecordWrongGuess() => Attempts++;

    public void Consume() => UsedAt = DateTimeOffset.UtcNow;

    /// <summary>A newer code replaces this one; it can no longer be used.</summary>
    public void Supersede() => UsedAt ??= DateTimeOffset.UtcNow;
}
