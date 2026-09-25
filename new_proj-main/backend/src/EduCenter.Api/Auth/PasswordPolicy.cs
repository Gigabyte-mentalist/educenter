namespace EduCenter.Api.Auth;

/// <summary>What a password must look like, in one place — creating a user, converting a lead and registering a student
/// used to each carry their own copy of the length check.</summary>
public static class PasswordPolicy
{
    public const int MinLength = 8;

    /// <returns>A user-facing problem, or null when the password is acceptable.</returns>
    public static string? Validate(string? password) =>
        string.IsNullOrWhiteSpace(password) || password.Length < MinLength
            ? $"Parol kamida {MinLength} belgidan iborat bo'lishi kerak."
            : null;
}
