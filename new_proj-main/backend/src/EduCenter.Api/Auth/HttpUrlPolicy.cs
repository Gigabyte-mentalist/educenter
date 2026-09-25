namespace EduCenter.Api.Auth;

/// <summary>What a link a user pastes for others to click has to look like. Only plain http(s) is safe: the same text is
/// rendered as an <c>href</c> for teachers and administrators, so <c>javascript:</c> or <c>data:</c> here would run the
/// author's code inside the reader's signed-in session.</summary>
public static class HttpUrlPolicy
{
    /// <summary>Length of the widest column that stores such a link (homework submissions).</summary>
    public const int MaxLength = 1000;

    public static bool IsValid(string? url) =>
        url is { Length: > 0 and <= MaxLength }
        && Uri.TryCreate(url, UriKind.Absolute, out var parsed)
        && (parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeHttp)
        && !string.IsNullOrEmpty(parsed.Host)
        && string.IsNullOrEmpty(parsed.UserInfo);
}
