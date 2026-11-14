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

public class AuthServiceIntegrationTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly IAuthService _authService;
    private readonly ITokenService _tokenService;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly List<Guid> _createdUserIds = new();

    public AuthServiceIntegrationTests()
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
        _passwordHasher = new PasswordHasher<User>();
        _authService = new AuthService(_context, _tokenService, _passwordHasher, NullLogger<AuthService>.Instance);
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

    // 1. Successful Registration
    [Fact]
    public async Task Register_Successful_Creates_User_And_RefreshToken_In_PostgreSQL()
    {
        var email = $"register-{Guid.NewGuid():N}@example.com";
        var request = new RegisterRequestDto
        {
            FullName = "Jane Doe",
            Email = email,
            Password = "P@ssword123!Secure",
            ConfirmPassword = "P@ssword123!Secure"
        };

        var result = await _authService.RegisterAsync(request);

        Assert.True(result.Succeeded);
        Assert.Equal(StatusCodes.Status201Created, result.StatusCode);
        Assert.NotNull(result.Response);
        Assert.False(string.IsNullOrWhiteSpace(result.Response.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(result.RawRefreshToken));

        _createdUserIds.Add(result.Response.User.Id);

        // Verify PostgreSQL persistence
        var userInDb = await _context.Users
            .Include(u => u.RefreshTokens)
            .FirstOrDefaultAsync(u => u.Id == result.Response.User.Id);

        Assert.NotNull(userInDb);
        Assert.Equal("Jane Doe", userInDb.FullName);
        Assert.Equal(email, userInDb.Email);
        Assert.Equal(email.ToUpperInvariant(), userInDb.NormalizedEmail);
        Assert.True(userInDb.IsActive);
        Assert.Single(userInDb.RefreshTokens);
    }

    // 2. Duplicate Email
    [Fact]
    public async Task Register_Duplicate_Email_Returns_Conflict()
    {
        var email = $"duplicate-{Guid.NewGuid():N}@example.com";
        var request = new RegisterRequestDto
        {
            FullName = "User One",
            Email = email,
            Password = "P@ssword123!Secure",
            ConfirmPassword = "P@ssword123!Secure"
        };

        var firstResult = await _authService.RegisterAsync(request);
        Assert.True(firstResult.Succeeded);
        _createdUserIds.Add(firstResult.Response!.User.Id);

        // Attempt second registration with exact same email
        var duplicateResult = await _authService.RegisterAsync(request);

        Assert.False(duplicateResult.Succeeded);
        Assert.Equal(StatusCodes.Status409Conflict, duplicateResult.StatusCode);
        Assert.Equal("Duplicate Email", duplicateResult.ErrorTitle);
    }

    // 3. Case-Insensitive Duplicate Email
    [Fact]
    public async Task Register_Case_Insensitive_Duplicate_Email_Returns_Conflict()
    {
        var baseEmail = $"case-test-{Guid.NewGuid():N}";
        var lowercaseEmail = $"{baseEmail}@example.com";
        var mixedCaseEmail = $"{baseEmail.ToUpperInvariant()}@EXAMPLE.COM";

        var firstResult = await _authService.RegisterAsync(new RegisterRequestDto
        {
            FullName = "Lower Case User",
            Email = lowercaseEmail,
            Password = "P@ssword123!Secure",
            ConfirmPassword = "P@ssword123!Secure"
        });
        Assert.True(firstResult.Succeeded);
        _createdUserIds.Add(firstResult.Response!.User.Id);

        // Attempt second registration with uppercase/mixed email
        var secondResult = await _authService.RegisterAsync(new RegisterRequestDto
        {
            FullName = "Upper Case User",
            Email = mixedCaseEmail,
            Password = "P@ssword123!Secure",
            ConfirmPassword = "P@ssword123!Secure"
        });

        Assert.False(secondResult.Succeeded);
        Assert.Equal(StatusCodes.Status409Conflict, secondResult.StatusCode);
        Assert.Equal("Duplicate Email", secondResult.ErrorTitle);
    }

    // 4. Password Hashing (Never stored in plaintext)
    [Fact]
    public async Task Register_Hashes_Password_Never_Stores_Plaintext()
    {
        var plainPassword = "SuperSecretP@ssword123!";
        var email = $"hasher-test-{Guid.NewGuid():N}@example.com";

        var result = await _authService.RegisterAsync(new RegisterRequestDto
        {
            FullName = "Hashing Test",
            Email = email,
            Password = plainPassword,
            ConfirmPassword = plainPassword
        });

        Assert.True(result.Succeeded);
        _createdUserIds.Add(result.Response!.User.Id);

        var userInDb = await _context.Users.FindAsync(result.Response.User.Id);
        Assert.NotNull(userInDb);
        Assert.NotEqual(plainPassword, userInDb.PasswordHash);
        Assert.DoesNotContain(plainPassword, userInDb.PasswordHash);

        // Verify valid hash using password hasher
        var verifyResult = _passwordHasher.VerifyHashedPassword(userInDb, userInDb.PasswordHash, plainPassword);
        Assert.Equal(PasswordVerificationResult.Success, verifyResult);
    }

    // 5. Successful Login
    [Fact]
    public async Task Login_Successful_Returns_Tokens_And_Resets_Failed_Attempts()
    {
        var email = $"login-success-{Guid.NewGuid():N}@example.com";
        var password = "P@ssword123!Secure";

        var regResult = await _authService.RegisterAsync(new RegisterRequestDto
        {
            FullName = "Login Success",
            Email = email,
            Password = password,
            ConfirmPassword = password
        });
        _createdUserIds.Add(regResult.Response!.User.Id);

        // Simulate prior failed attempt
        var userInDb = await _context.Users.FindAsync(regResult.Response.User.Id);
        userInDb!.AccessFailedCount = 2;
        await _context.SaveChangesAsync();

        // Perform login
        var loginResult = await _authService.LoginAsync(new LoginRequestDto
        {
            Email = email,
            Password = password
        });

        Assert.True(loginResult.Succeeded);
        Assert.Equal(StatusCodes.Status200OK, loginResult.StatusCode);
        Assert.NotNull(loginResult.Response);
        Assert.False(string.IsNullOrWhiteSpace(loginResult.Response.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(loginResult.RawRefreshToken));

        // Verify AccessFailedCount reset in PostgreSQL
        var refreshedUser = await _context.Users.FindAsync(regResult.Response.User.Id);
        Assert.Equal(0, refreshedUser!.AccessFailedCount);
        Assert.Null(refreshedUser.LockoutEndUtc);
    }

    // 6. Incorrect Password
    [Fact]
    public async Task Login_Incorrect_Password_Returns_Unauthorized_And_Increments_Failed_Count()
    {
        var email = $"login-fail-{Guid.NewGuid():N}@example.com";
        var password = "P@ssword123!Secure";

        var regResult = await _authService.RegisterAsync(new RegisterRequestDto
        {
            FullName = "Login Fail",
            Email = email,
            Password = password,
            ConfirmPassword = password
        });
        _createdUserIds.Add(regResult.Response!.User.Id);

        var loginResult = await _authService.LoginAsync(new LoginRequestDto
        {
            Email = email,
            Password = "WrongPassword!"
        });

        Assert.False(loginResult.Succeeded);
        Assert.Equal(StatusCodes.Status401Unauthorized, loginResult.StatusCode);
        Assert.Equal("Invalid Credentials", loginResult.ErrorTitle);

        // Verify failed attempt incremented in database
        var refreshedUser = await _context.Users.FindAsync(regResult.Response.User.Id);
        Assert.Equal(1, refreshedUser!.AccessFailedCount);
    }

    // 7. Unknown Email
    [Fact]
    public async Task Login_Unknown_Email_Returns_Unauthorized_Generic_Message()
    {
        var loginResult = await _authService.LoginAsync(new LoginRequestDto
        {
            Email = $"nonexistent-{Guid.NewGuid():N}@example.com",
            Password = "AnyPassword123!"
        });

        Assert.False(loginResult.Succeeded);
        Assert.Equal(StatusCodes.Status401Unauthorized, loginResult.StatusCode);
        Assert.Equal("Invalid Credentials", loginResult.ErrorTitle);
        Assert.Equal("Invalid email or password.", loginResult.ErrorDetail);
    }

    // 8. Inactive Account
    [Fact]
    public async Task Login_Inactive_Account_Returns_Forbidden()
    {
        var email = $"inactive-{Guid.NewGuid():N}@example.com";
        var password = "P@ssword123!Secure";

        var regResult = await _authService.RegisterAsync(new RegisterRequestDto
        {
            FullName = "Inactive User",
            Email = email,
            Password = password,
            ConfirmPassword = password
        });
        _createdUserIds.Add(regResult.Response!.User.Id);

        // Deactivate account directly in database
        var userInDb = await _context.Users.FindAsync(regResult.Response.User.Id);
        userInDb!.IsActive = false;
        await _context.SaveChangesAsync();

        var loginResult = await _authService.LoginAsync(new LoginRequestDto
        {
            Email = email,
            Password = password
        });

        Assert.False(loginResult.Succeeded);
        Assert.Equal(StatusCodes.Status403Forbidden, loginResult.StatusCode);
        Assert.Equal("Account Inactive", loginResult.ErrorTitle);
    }

    // 9. Lockout After Repeated Failures
    [Fact]
    public async Task Login_Locks_Out_Account_After_Five_Consecutive_Failures()
    {
        var email = $"lockout-{Guid.NewGuid():N}@example.com";
        var correctPassword = "P@ssword123!Secure";

        var regResult = await _authService.RegisterAsync(new RegisterRequestDto
        {
            FullName = "Lockout Candidate",
            Email = email,
            Password = correctPassword,
            ConfirmPassword = correctPassword
        });
        _createdUserIds.Add(regResult.Response!.User.Id);

        // Perform 4 failed attempts (should return 401)
        for (int i = 1; i <= 4; i++)
        {
            var failResult = await _authService.LoginAsync(new LoginRequestDto
            {
                Email = email,
                Password = "WrongPassword!"
            });
            Assert.Equal(StatusCodes.Status401Unauthorized, failResult.StatusCode);
        }

        // 5th failed attempt triggers account lockout (should return 423 Locked)
        var fifthAttempt = await _authService.LoginAsync(new LoginRequestDto
        {
            Email = email,
            Password = "WrongPassword!"
        });

        Assert.False(fifthAttempt.Succeeded);
        Assert.Equal(StatusCodes.Status423Locked, fifthAttempt.StatusCode);
        Assert.Equal("Account Locked", fifthAttempt.ErrorTitle);

        // Verify lockout end in PostgreSQL
        var lockedUser = await _context.Users.FindAsync(regResult.Response.User.Id);
        Assert.NotNull(lockedUser!.LockoutEndUtc);
        Assert.True(lockedUser.LockoutEndUtc > DateTime.UtcNow);

        // Even with the CORRECT password, locked user is rejected until lockout expires
        var attemptWithCorrectPassword = await _authService.LoginAsync(new LoginRequestDto
        {
            Email = email,
            Password = correctPassword
        });

        Assert.False(attemptWithCorrectPassword.Succeeded);
        Assert.Equal(StatusCodes.Status423Locked, attemptWithCorrectPassword.StatusCode);
        Assert.Equal("Account Locked", attemptWithCorrectPassword.ErrorTitle);
    }

    // 12. Refresh-Token Hash Persistence
    [Fact]
    public async Task Refresh_Token_Hash_Is_Persisted_Raw_Token_Never_Stored()
    {
        var email = $"refresh-hash-{Guid.NewGuid():N}@example.com";
        var password = "P@ssword123!Secure";

        var regResult = await _authService.RegisterAsync(new RegisterRequestDto
        {
            FullName = "Token Storage User",
            Email = email,
            Password = password,
            ConfirmPassword = password
        });
        _createdUserIds.Add(regResult.Response!.User.Id);

        var rawRefreshToken = regResult.RawRefreshToken!;
        var expectedHash = _tokenService.HashToken(rawRefreshToken);

        // Look up token in PostgreSQL
        var tokenInDb = await _context.RefreshTokens
            .FirstOrDefaultAsync(t => t.UserId == regResult.Response.User.Id);

        Assert.NotNull(tokenInDb);
        Assert.Equal(expectedHash, tokenInDb.TokenHash);
        Assert.NotEqual(rawRefreshToken, tokenInDb.TokenHash);
        Assert.DoesNotContain(rawRefreshToken, tokenInDb.TokenHash);
    }
}
