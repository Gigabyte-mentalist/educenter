using System.Security.Cryptography;

namespace EduCenter.Api.Auth;

/// <summary>A temporary password for <c>POST /api/users/{id}/reset-password</c> when the caller leaves one
/// unspecified — same alphabet as <c>frontend/src/lib/generate-password.ts</c> (no 0/O/1/l/I: read aloud or
/// copied off a screen without ambiguity) so a generated password looks the same wherever it comes from.</summary>
public static class PasswordGenerator
{
    private const string Alphabet = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string Generate(int length = 12)
    {
        Span<char> chars = stackalloc char[length];
        var bytes = RandomNumberGenerator.GetBytes(length * 4);
        for (var i = 0; i < length; i++)
        {
            var value = BitConverter.ToUInt32(bytes.AsSpan(i * 4, 4));
            chars[i] = Alphabet[(int)(value % Alphabet.Length)];
        }
        return new string(chars);
    }
}
