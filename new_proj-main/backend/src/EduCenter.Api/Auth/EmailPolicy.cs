using System.Net.Mail;

namespace EduCenter.Api.Auth;

/// <summary>What counts as a usable login email, in one place — registering a student and linking a parent
/// used to each carry their own copy of the check.</summary>
public static class EmailPolicy
{
    public const int MaxLength = 200;

    public static bool IsValid(string email) =>
        email.Length is > 0 and <= MaxLength && MailAddress.TryCreate(email, out var parsed) && parsed.Address == email;
}
