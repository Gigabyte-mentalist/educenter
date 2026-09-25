namespace EduCenter.Api.Auth;

public record LoginRequest(string Email, string Password);
public record RefreshRequest(string RefreshToken);
public record LogoutRequest(string RefreshToken);

public record TokenResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, bool MustChangePassword);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <param name="Identifier">The login email, or a phone number.</param>
public record ForgotPasswordRequest(string Identifier);
public record ResetPasswordRequest(string Identifier, string Code, string NewPassword);
