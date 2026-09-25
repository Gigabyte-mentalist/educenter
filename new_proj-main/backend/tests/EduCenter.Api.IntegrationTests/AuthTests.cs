using System.Net;
using System.Net.Http.Json;
using EduCenter.Infrastructure.Seed;

namespace EduCenter.Api.IntegrationTests;

[Collection("Api")]
public class AuthTests(EduCenterWebApplicationFactory factory)
{
    [Fact]
    public async Task Login_WithSeededDemoAccount_ReturnsTokens()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "superadmin@demo-center.test",
            password = DbSeeder.DemoPassword
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<TokenResponseDto>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body!.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(body.RefreshToken));
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "superadmin@demo-center.test",
            password = "not-the-real-password"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithLoginRefreshToken_IssuesNewTokenPair()
    {
        var client = factory.CreateClient();

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "teacher@demo-center.test",
            password = DbSeeder.DemoPassword
        });
        var loginBody = await loginResponse.Content.ReadAsStringAsync();
        Assert.True(loginResponse.IsSuccessStatusCode,
            $"login failed: {(int)loginResponse.StatusCode} {loginResponse.StatusCode} body='{loginBody}'");
        var tokens = System.Text.Json.JsonSerializer.Deserialize<TokenResponseDto>(loginBody,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        var refreshResponse = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = tokens!.RefreshToken });

        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        var newTokens = await refreshResponse.Content.ReadFromJsonAsync<TokenResponseDto>();
        Assert.NotEqual(tokens.RefreshToken, newTokens!.RefreshToken); // rotation: old token is single-use
    }

    private record TokenResponseDto(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken);
}
