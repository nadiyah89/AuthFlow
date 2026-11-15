using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AuthFlow.Api.Data;
using AuthFlow.Api.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AuthFlow.IntegrationTests;

public class ProtectedEndpointsAndCsrfHttpTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly HttpClient _client;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly List<Guid> _createdUserIds = new();

    public ProtectedEndpointsAndCsrfHttpTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public void Dispose()
    {
        if (_createdUserIds.Count > 0)
        {
            using var scope = _factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var usersToDelete = context.Users
                .Where(u => _createdUserIds.Contains(u.Id))
                .ToList();

            if (usersToDelete.Count > 0)
            {
                context.Users.RemoveRange(usersToDelete);
                context.SaveChanges();
            }
        }
    }

    private async Task<(string accessToken, string refreshCookieHeader, Guid userId)> RegisterTestUserAsync(string prefix)
    {
        var email = $"{prefix}-{Guid.NewGuid():N}@example.com";
        var response = await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequestDto
        {
            FullName = "HTTP Protected User",
            Email = email,
            Password = "P@ssword123!Secure",
            ConfirmPassword = "P@ssword123!Secure"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var authResponse = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        _createdUserIds.Add(authResponse!.User.Id);

        var cookieHeader = response.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("refreshToken="));
        var rawCookie = cookieHeader.Split(';')[0]; // "refreshToken=..."

        return (authResponse.AccessToken, rawCookie, authResponse.User.Id);
    }

    // 10. GET /api/auth/me with a valid JWT
    [Fact]
    public async Task GetMe_With_Valid_Jwt_Returns_200_With_Profile()
    {
        var (accessToken, _, userId) = await RegisterTestUserAsync("me-valid");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var profile = await response.Content.ReadFromJsonAsync<UserProfileDto>();
        Assert.NotNull(profile);
        Assert.Equal(userId, profile.Id);
        Assert.Equal("HTTP Protected User", profile.FullName);
    }

    // 11. GET /api/auth/me without a JWT
    [Fact]
    public async Task GetMe_Without_Jwt_Returns_401_Unauthorized()
    {
        var response = await _client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // 12. GET /api/auth/me with an invalid JWT
    [Fact]
    public async Task GetMe_With_Invalid_Jwt_Returns_401_Unauthorized()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "invalid.forged.jwt.token");

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // 13. CSRF Missing-Header Rejection
    [Fact]
    public async Task Csrf_Missing_Header_Rejection_Returns_403_Forbidden()
    {
        var (_, rawCookie, _) = await RegisterTestUserAsync("csrf-no-header");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", rawCookie);
        request.Headers.Add("Origin", "http://localhost:5173");
        // Omit X-Requested-With / X-AuthFlow-Client

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("CSRF Validation Failed", problem.Title);
        Assert.Contains("Missing required security header", problem.Detail);
    }

    // 14. CSRF Untrusted-Origin Rejection
    [Fact]
    public async Task Csrf_Untrusted_Origin_Rejection_Returns_403_Forbidden()
    {
        var (_, rawCookie, _) = await RegisterTestUserAsync("csrf-bad-origin");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", rawCookie);
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        request.Headers.Add("Origin", "http://attacker-evil-site.com");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("CSRF Validation Failed", problem.Title);
        Assert.Contains("not authorized", problem.Detail);
    }

    // 15. Legitimate Trusted-Origin Request
    [Fact]
    public async Task Csrf_Legitimate_Trusted_Origin_And_Header_Succeeds()
    {
        var (_, rawCookie, _) = await RegisterTestUserAsync("csrf-legitimate");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", rawCookie);
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        request.Headers.Add("Origin", "http://localhost:5173");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify new access token returned
        var authResponse = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(authResponse);
        Assert.False(string.IsNullOrWhiteSpace(authResponse.AccessToken));

        // Verify new rotated cookie returned
        Assert.True(response.Headers.Contains("Set-Cookie"));
        var newCookie = response.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("refreshToken="));
        Assert.NotNull(newCookie);
        Assert.Contains("httponly", newCookie.ToLowerInvariant());
    }

    // 16. CSRF Malicious Hostname Suffix Rejection (Defends against suffix bypass vulnerabilities)
    [Fact]
    public async Task Csrf_Malicious_Hostname_Suffix_Rejection_Returns_403_Forbidden()
    {
        var (_, rawCookie, _) = await RegisterTestUserAsync("csrf-suffix-attack");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", rawCookie);
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        // Malicious origin attempting to exploit suffix matching on localhost
        request.Headers.Add("Origin", "http://attacker-localhost");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("CSRF Validation Failed", problem.Title);
        Assert.Contains("not authorized", problem.Detail);
    }

    // 17. CSRF Malformed Origin URI Rejection
    [Fact]
    public async Task Csrf_Malformed_Origin_Header_Returns_403_Forbidden()
    {
        var (_, rawCookie, _) = await RegisterTestUserAsync("csrf-malformed");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", rawCookie);
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        request.Headers.Add("Origin", "not-a-valid-absolute-uri");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("CSRF Validation Failed", problem.Title);
    }

    // 18. Configured Production Origin Allowed via Configuration
    [Fact]
    public async Task Csrf_Configured_Production_Origin_Succeeds()
    {
        var productionOrigin = "https://authflow-app.vercel.app";

        var customFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("CorsSettings:AllowedOrigins:0", productionOrigin);
        });

        var customClient = customFactory.CreateClient();

        // Register user via custom client
        var email = $"prod-origin-{Guid.NewGuid():N}@example.com";
        var regResponse = await customClient.PostAsJsonAsync("/api/auth/register", new RegisterRequestDto
        {
            FullName = "Production Origin User",
            Email = email,
            Password = "P@ssword123!Secure",
            ConfirmPassword = "P@ssword123!Secure"
        });

        Assert.Equal(HttpStatusCode.Created, regResponse.StatusCode);
        var authResponse = await regResponse.Content.ReadFromJsonAsync<AuthResponseDto>();
        _createdUserIds.Add(authResponse!.User.Id);

        var cookieHeader = regResponse.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("refreshToken="));
        var rawCookie = cookieHeader.Split(';')[0];

        // Send refresh with production origin
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", rawCookie);
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        request.Headers.Add("Origin", productionOrigin);

        var refreshResponse = await customClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        var refreshResult = await refreshResponse.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(refreshResult);
        Assert.False(string.IsNullOrWhiteSpace(refreshResult.AccessToken));
    }
}
