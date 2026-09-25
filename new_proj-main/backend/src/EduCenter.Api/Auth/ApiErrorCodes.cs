namespace EduCenter.Api.Auth;

/// <summary>Machine-readable error codes a client branches on — the human-facing text in the same response body
/// is for display only and can change without breaking that logic.</summary>
public static class ApiErrorCodes
{
    public const string AccountBlocked = "ACCOUNT_BLOCKED";
    public const string PasswordChangeRequired = "PASSWORD_CHANGE_REQUIRED";

    /// <summary>Shown with <see cref="AccountBlocked"/> by every route that can answer it — login, refresh,
    /// change-password and the status middleware — so the wording cannot drift between them.</summary>
    public const string AccountBlockedMessage =
        "Hisobingiz bloklangan. Batafsil ma'lumot uchun markaz administratoriga murojaat qiling.";
}
