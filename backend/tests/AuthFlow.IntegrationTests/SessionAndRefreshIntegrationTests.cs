using AuthFlow.Api.Data;
using AuthFlow.Api.DTOs;
using AuthFlow.Api.Models;
using AuthFlow.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AuthFlow.IntegrationTests;

public class SessionAndRefreshIntegrationTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly IAuthService _authService;
    private readonly ITokenService _tokenService;
    private readonly List<Guid> _createdUserIds = new();

    public SessionAndRefreshIntegrationTests()
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets(typeof(AppDbContext).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = config.GetConnectionString("DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=authflow_dev;Username=postgres;";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        _context = new AppDbContext(options);

        var jwtSettings = new JwtSettings
        {
            Secret = "AuthFlow_Super_Secure_Dev_Secret_Key_2026_Minimum_32_Bytes_Long_Secret!",
            Issuer = "AuthFlow.Api",
            Audience = "AuthFlow.Client",
            AccessTokenExpirationMinutes = 15,
            RefreshTokenExpirationDays = 7
        };

        _tokenService = new TokenService(Options.Create(jwtSettings));
        var passwordHasher = new PasswordHasher<User>();
        _authService = new AuthService(_context, _tokenService, passwordHasher, NullLogger<AuthService>.Instance);
    }

    public void Dispose()
    {
        if (_createdUserIds.Count > 0)
        {
            var usersToDelete = _context.Users
                .Where(u => _createdUserIds.Contains(u.Id))
                .ToList();

            if (usersToDelete.Count > 0)
            {
                _context.Users.RemoveRange(usersToDelete);
                _context.SaveChanges();
            }
        }
        _context.Dispose();
    }

    private async Task<(User user, string rawRefreshToken)> RegisterTestUserAsync(string prefix)
    {
        var email = $"{prefix}-{Guid.NewGuid():N}@example.com";
        var reg = await _authService.RegisterAsync(new RegisterRequestDto
        {
            FullName = "Test Session User",
            Email = email,
            Password = "P@ssword123!Secure",
            ConfirmPassword = "P@ssword123!Secure"
        });

        _createdUserIds.Add(reg.Response!.User.Id);
        var user = await _context.Users.FindAsync(reg.Response.User.Id);
        return (user!, reg.RawRefreshToken!);
    }

    // 1. Successful Refresh
    [Fact]
    public async Task Refresh_Successful_Rotates_Token_And_Issues_New_Access_Token()
    {
        var (user, originalRawToken) = await RegisterTestUserAsync("refresh-success");

        // Rotate token
        var refreshResult = await _authService.RefreshTokenAsync(originalRawToken);

        Assert.True(refreshResult.Succeeded);
        Assert.Equal(StatusCodes.Status200OK, refreshResult.StatusCode);
        Assert.NotNull(refreshResult.Response);
        Assert.False(string.IsNullOrWhiteSpace(refreshResult.Response.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(refreshResult.RawRefreshToken));
        Assert.NotEqual(originalRawToken, refreshResult.RawRefreshToken);

        // Verify in PostgreSQL: original token is revoked and points to successor
        var originalHash = _tokenService.HashToken(originalRawToken);
        var originalInDb = await _context.RefreshTokens.SingleAsync(t => t.TokenHash == originalHash);

        Assert.NotNull(originalInDb.RevokedAtUtc);
        Assert.NotNull(originalInDb.ReplacedByTokenId);

        // Verify successor token in PostgreSQL
        var successorHash = _tokenService.HashToken(refreshResult.RawRefreshToken);
        var successorInDb = await _context.RefreshTokens.SingleAsync(t => t.TokenHash == successorHash);

        Assert.Null(successorInDb.RevokedAtUtc);
        Assert.Equal(originalInDb.FamilyId, successorInDb.FamilyId); // Inherited family!
        Assert.Equal(originalInDb.ReplacedByTokenId, successorInDb.Id);
    }

    // 2. Expired Refresh Token
    [Fact]
    public async Task Refresh_Expired_Token_Returns_Unauthorized()
    {
        var (user, rawToken) = await RegisterTestUserAsync("refresh-expired");
        var tokenHash = _tokenService.HashToken(rawToken);

        // Manually expire token in PostgreSQL
        var tokenInDb = await _context.RefreshTokens.SingleAsync(t => t.TokenHash == tokenHash);
        tokenInDb.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-5);
        await _context.SaveChangesAsync();

        var refreshResult = await _authService.RefreshTokenAsync(rawToken);

        Assert.False(refreshResult.Succeeded);
        Assert.Equal(StatusCodes.Status401Unauthorized, refreshResult.StatusCode);
    }

    // 3. Revoked Refresh Token & 4. Reuse Detection & 5. Family Revocation
    [Fact]
    public async Task Refresh_Token_Reuse_Detection_Revokes_Entire_Family()
    {
        var (user, token1) = await RegisterTestUserAsync("reuse-detection");

        // Step 1: Legitimate rotation (token1 -> token2)
        var rotateResult1 = await _authService.RefreshTokenAsync(token1);
        Assert.True(rotateResult1.Succeeded);
        var token2 = rotateResult1.RawRefreshToken!;

        // Step 2: Another legitimate rotation (token2 -> token3)
        var rotateResult2 = await _authService.RefreshTokenAsync(token2);
        Assert.True(rotateResult2.Succeeded);
        var token3 = rotateResult2.RawRefreshToken!;

        // Verify token3 is currently active
        var token3Hash = _tokenService.HashToken(token3);
        var token3InDb = await _context.RefreshTokens.SingleAsync(t => t.TokenHash == token3Hash);
        Assert.Null(token3InDb.RevokedAtUtc);

        // Step 3: ATTACK SIMULATION! An attacker replays revoked token1!
        var reuseAttackResult = await _authService.RefreshTokenAsync(token1);

        Assert.False(reuseAttackResult.Succeeded);
        Assert.Equal(StatusCodes.Status401Unauthorized, reuseAttackResult.StatusCode);

        // Step 4: Verify REUSE DETECTION revoked the entire family!
        // Even the legitimate token3 must now be revoked in PostgreSQL!
        _context.Entry(token3InDb).Reload();
        var token3AfterAttack = await _context.RefreshTokens.SingleAsync(t => t.TokenHash == token3Hash);
        Assert.NotNull(token3AfterAttack.RevokedAtUtc);

        // Legitimate user attempting to refresh with token3 is now rejected
        var legitimateUserAttempt = await _authService.RefreshTokenAsync(token3);
        Assert.False(legitimateUserAttempt.Succeeded);
        Assert.Equal(StatusCodes.Status401Unauthorized, legitimateUserAttempt.StatusCode);
    }

    // 6. Concurrent Refresh Requests Row-Level Serialization
    [Fact]
    public async Task Refresh_Concurrent_Requests_Serializes_And_Prevents_Multiple_Successors()
    {
        var (user, originalToken) = await RegisterTestUserAsync("concurrent-refresh");

        // Prepare 5 concurrent refresh requests on the exact same token
        var tasks = Enumerable.Range(0, 5).Select(_ => Task.Run(async () =>
        {
            // Use independent DbContext instances to simulate multiple concurrent HTTP requests
            var config = new ConfigurationBuilder()
                .AddUserSecrets(typeof(AppDbContext).Assembly, optional: true)
                .AddEnvironmentVariables()
                .Build();
            var cs = config.GetConnectionString("DefaultConnection")
                ?? "Host=localhost;Port=5432;Database=authflow_dev;Username=postgres;";
            var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).Options;
            using var localContext = new AppDbContext(options);

            var localAuthService = new AuthService(localContext, _tokenService, new PasswordHasher<User>(), NullLogger<AuthService>.Instance);
            return await localAuthService.RefreshTokenAsync(originalToken);
        })).ToArray();

        var results = await Task.WhenAll(tasks);

        // Exactly ONE request must succeed; all others must be rejected due to row lock and revocation!
        var successCount = results.Count(r => r.Succeeded);
        var failureCount = results.Count(r => !r.Succeeded && r.StatusCode == StatusCodes.Status401Unauthorized);

        Assert.Equal(1, successCount);
        Assert.Equal(4, failureCount);

        // Verify in PostgreSQL: original token was rotated exactly once
        var originalHash = _tokenService.HashToken(originalToken);
        var originalInDb = await _context.RefreshTokens.AsNoTracking().SingleAsync(t => t.TokenHash == originalHash);
        Assert.NotNull(originalInDb.RevokedAtUtc);

        // Verify total tokens in family
        var totalTokensInFamily = await _context.RefreshTokens
            .AsNoTracking()
            .Where(t => t.FamilyId == originalInDb.FamilyId)
            .CountAsync();

        Assert.Equal(2, totalTokensInFamily); // Exactly original (1) + successor (1)
    }

    // 7. Successful Logout
    [Fact]
    public async Task Logout_Successful_Revokes_Refresh_Token()
    {
        var (user, rawToken) = await RegisterTestUserAsync("logout-success");
        var tokenHash = _tokenService.HashToken(rawToken);

        var logoutResult = await _authService.LogoutAsync(rawToken);
        Assert.True(logoutResult.Succeeded);

        // Verify token in PostgreSQL is revoked
        var tokenInDb = await _context.RefreshTokens.SingleAsync(t => t.TokenHash == tokenHash);
        Assert.NotNull(tokenInDb.RevokedAtUtc);
    }

    // 8. Logout Without a Cookie
    [Fact]
    public async Task Logout_Without_Cookie_Succeeds_Idempotently()
    {
        var logoutResult = await _authService.LogoutAsync(null);
        Assert.True(logoutResult.Succeeded);
        Assert.Equal(StatusCodes.Status200OK, logoutResult.StatusCode);
    }

    // 9. Refresh After Logout
    [Fact]
    public async Task Refresh_After_Logout_Returns_Unauthorized()
    {
        var (user, rawToken) = await RegisterTestUserAsync("refresh-after-logout");

        // Logout
        var logoutResult = await _authService.LogoutAsync(rawToken);
        Assert.True(logoutResult.Succeeded);

        // Attempt refresh with logged out token
        var refreshResult = await _authService.RefreshTokenAsync(rawToken);
        Assert.False(refreshResult.Succeeded);
        Assert.Equal(StatusCodes.Status401Unauthorized, refreshResult.StatusCode);
    }
}
