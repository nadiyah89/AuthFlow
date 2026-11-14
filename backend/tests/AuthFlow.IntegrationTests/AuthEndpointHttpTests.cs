using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuthFlow.Api.Data;
using AuthFlow.Api.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AuthFlow.IntegrationTests;

public class AuthEndpointHttpTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly HttpClient _client;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly List<Guid> _createdUserIds = new();

    public AuthEndpointHttpTests(WebApplicationFactory<Program> factory)
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

    [Fact]
    public async Task Http_Register_Valid_Request_Returns_201_With_Cookie_And_Token()
    {
        var email = $"http-reg-{Guid.NewGuid():N}@example.com";
        var request = new RegisterRequestDto
        {
            FullName = "HTTP Test User",
            Email = email,
            Password = "P@ssword123!Secure",
            ConfirmPassword = "P@ssword123!Secure"
        };

        var response = await _client.PostAsJsonAsync("/api/auth/register", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // Verify Set-Cookie header contains refreshToken and HttpOnly
        Assert.True(response.Headers.Contains("Set-Cookie"));
        var cookies = response.Headers.GetValues("Set-Cookie").ToList();
        var refreshCookie = cookies.FirstOrDefault(c => c.StartsWith("refreshToken="));
        Assert.NotNull(refreshCookie);
        Assert.Contains("httponly", refreshCookie.ToLowerInvariant());
        Assert.Contains("path=/api/auth", refreshCookie.ToLowerInvariant());

        // Verify JSON response body
        var content = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(content);
        Assert.False(string.IsNullOrWhiteSpace(content.AccessToken));
        Assert.Equal("HTTP Test User", content.User.FullName);
        Assert.Equal(email, content.User.Email);

        _createdUserIds.Add(content.User.Id);
    }

    [Fact]
    public async Task Http_Register_Password_Mismatch_Returns_400_BadRequest()
    {
        var request = new RegisterRequestDto
        {
            FullName = "Mismatch User",
            Email = $"mismatch-{Guid.NewGuid():N}@example.com",
            Password = "P@ssword123!Secure",
            ConfirmPassword = "DifferentPassword123!"
        };

        var response = await _client.PostAsJsonAsync("/api/auth/register", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Http_Register_Duplicate_Email_Returns_409_Conflict()
    {
        var email = $"http-dup-{Guid.NewGuid():N}@example.com";
        var request = new RegisterRequestDto
        {
            FullName = "First User",
            Email = email,
            Password = "P@ssword123!Secure",
            ConfirmPassword = "P@ssword123!Secure"
        };

        var firstResponse = await _client.PostAsJsonAsync("/api/auth/register", request);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        var content = await firstResponse.Content.ReadFromJsonAsync<AuthResponseDto>();
        _createdUserIds.Add(content!.User.Id);

        // Second register attempt with duplicate email
        var duplicateResponse = await _client.PostAsJsonAsync("/api/auth/register", request);
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);

        var problem = await duplicateResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Duplicate Email", problem.Title);
    }

    [Fact]
    public async Task Http_Login_Valid_Credentials_Returns_200_With_Tokens()
    {
        var email = $"http-login-{Guid.NewGuid():N}@example.com";
        var password = "P@ssword123!Secure";

        var regResponse = await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequestDto
        {
            FullName = "Login HTTP User",
            Email = email,
            Password = password,
            ConfirmPassword = password
        });
        var regContent = await regResponse.Content.ReadFromJsonAsync<AuthResponseDto>();
        _createdUserIds.Add(regContent!.User.Id);

        // Perform HTTP Login
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Email = email,
            Password = password
        });

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var loginContent = await loginResponse.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(loginContent);
        Assert.False(string.IsNullOrWhiteSpace(loginContent.AccessToken));
        Assert.Equal(email, loginContent.User.Email);

        // Check refresh cookie in login response
        Assert.True(loginResponse.Headers.Contains("Set-Cookie"));
        var cookies = loginResponse.Headers.GetValues("Set-Cookie").ToList();
        var refreshCookie = cookies.FirstOrDefault(c => c.StartsWith("refreshToken="));
        Assert.NotNull(refreshCookie);
        Assert.Contains("httponly", refreshCookie.ToLowerInvariant());
    }

    [Fact]
    public async Task Http_Login_Invalid_Password_Returns_401_Unauthorized()
    {
        var email = $"http-fail-{Guid.NewGuid():N}@example.com";
        var password = "P@ssword123!Secure";

        var regResponse = await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequestDto
        {
            FullName = "Login Fail User",
            Email = email,
            Password = password,
            ConfirmPassword = password
        });
        var regContent = await regResponse.Content.ReadFromJsonAsync<AuthResponseDto>();
        _createdUserIds.Add(regContent!.User.Id);

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Email = email,
            Password = "WrongPassword!"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, loginResponse.StatusCode);
        var problem = await loginResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Invalid Credentials", problem.Title);
    }
}
