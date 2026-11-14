using AuthFlow.Api.Data;
using AuthFlow.Api.DTOs;
using AuthFlow.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuthFlow.Api.Services;

public class AuthService : IAuthService
{
    private const int MaxFailedAccessAttempts = 5;
    private const int LockoutDurationMinutes = 15;

    // Static dummy hash used to neutralize timing attacks when an email does not exist
    private static readonly string DummyHash = new PasswordHasher<User>().HashPassword(new User(), "DummyPassword123!");

    private readonly AppDbContext _context;
    private readonly ITokenService _tokenService;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        AppDbContext context,
        ITokenService tokenService,
        IPasswordHasher<User> passwordHasher,
        ILogger<AuthService> logger)
    {
        _context = context;
        _tokenService = tokenService;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task<AuthResult> RegisterAsync(RegisterRequestDto request)
    {
        var normalizedEmail = request.Email.Trim().ToUpperInvariant();

        // 1. Check if email already exists
        var existingUser = await _context.Users.AnyAsync(u => u.NormalizedEmail == normalizedEmail);
        if (existingUser)
        {
            return AuthResult.Failure(
                StatusCodes.Status409Conflict,
                "Duplicate Email",
                "An account with this email address already exists.");
        }

        // 2. Instantiate and hash password
        var user = new User
        {
            Id = Guid.NewGuid(),
            FullName = request.FullName.Trim(),
            Email = request.Email.Trim(),
            NormalizedEmail = normalizedEmail,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            AccessFailedCount = 0,
            LockoutEndUtc = null
        };

        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

        // 3. Generate initial session refresh token
        var (rawRefreshToken, tokenHash, expiresAtUtc) = _tokenService.GenerateRefreshToken();

        var refreshTokenEntity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = tokenHash,
            FamilyId = Guid.NewGuid(),
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = expiresAtUtc,
            RevokedAtUtc = null,
            ReplacedByTokenId = null
        };

        _context.Users.Add(user);
        _context.RefreshTokens.Add(refreshTokenEntity);

        // 4. Save to PostgreSQL with concurrency duplicate protection
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pgEx && pgEx.SqlState == "23505")
        {
            _logger.LogWarning("Concurrent duplicate registration collision intercepted for email: {Email}", normalizedEmail);
            return AuthResult.Failure(
                StatusCodes.Status409Conflict,
                "Duplicate Email",
                "An account with this email address already exists.");
        }

        // 5. Generate short-lived JWT access token
        var accessToken = _tokenService.GenerateAccessToken(user);

        var responseDto = new AuthResponseDto
        {
            AccessToken = accessToken,
            ExpiresInMinutes = 15,
            User = new UserProfileDto
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                CreatedAtUtc = user.CreatedAtUtc
            }
        };

        return AuthResult.Success(responseDto, rawRefreshToken, expiresAtUtc, StatusCodes.Status201Created);
    }

    public async Task<AuthResult> LoginAsync(LoginRequestDto request)
    {
        var normalizedEmail = request.Email.Trim().ToUpperInvariant();

        // 1. Look up user
        var user = await _context.Users.SingleOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail);

        // 2. Prevent account enumeration: if user does not exist, perform dummy hash check
        if (user == null)
        {
            _passwordHasher.VerifyHashedPassword(new User(), DummyHash, request.Password);
            return AuthResult.Failure(
                StatusCodes.Status401Unauthorized,
                "Invalid Credentials",
                "Invalid email or password.");
        }

        // 3. Check if account is inactive
        if (!user.IsActive)
        {
            return AuthResult.Failure(
                StatusCodes.Status403Forbidden,
                "Account Inactive",
                "This account is currently inactive. Please contact support.");
        }

        // 4. Check account lockout status
        if (user.LockoutEndUtc.HasValue && user.LockoutEndUtc.Value > DateTime.UtcNow)
        {
            var remainingMinutes = Math.Ceiling((user.LockoutEndUtc.Value - DateTime.UtcNow).TotalMinutes);
            return AuthResult.Failure(
                StatusCodes.Status423Locked,
                "Account Locked",
                $"Account is temporarily locked due to repeated failed login attempts. Please try again in {remainingMinutes} minute(s).");
        }

        // 5. Verify password
        var verification = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);

        if (verification == PasswordVerificationResult.Failed)
        {
            user.AccessFailedCount++;
            user.UpdatedAtUtc = DateTime.UtcNow;

            if (user.AccessFailedCount >= MaxFailedAccessAttempts)
            {
                user.LockoutEndUtc = DateTime.UtcNow.AddMinutes(LockoutDurationMinutes);
                await _context.SaveChangesAsync();

                _logger.LogWarning("Account locked due to consecutive failed logins: UserId={UserId}", user.Id);
                return AuthResult.Failure(
                    StatusCodes.Status423Locked,
                    "Account Locked",
                    $"Account is temporarily locked due to {MaxFailedAccessAttempts} failed login attempts. Please try again in {LockoutDurationMinutes} minutes.");
            }

            await _context.SaveChangesAsync();
            return AuthResult.Failure(
                StatusCodes.Status401Unauthorized,
                "Invalid Credentials",
                "Invalid email or password.");
        }

        // 6. Login succeeded: Reset failed attempts and lockout
        user.AccessFailedCount = 0;
        user.LockoutEndUtc = null;
        user.UpdatedAtUtc = DateTime.UtcNow;

        // 7. Issue new refresh token
        var (rawRefreshToken, tokenHash, expiresAtUtc) = _tokenService.GenerateRefreshToken();

        var refreshTokenEntity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = tokenHash,
            FamilyId = Guid.NewGuid(),
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = expiresAtUtc,
            RevokedAtUtc = null,
            ReplacedByTokenId = null
        };

        _context.RefreshTokens.Add(refreshTokenEntity);
        await _context.SaveChangesAsync();

        // 8. Generate JWT access token
        var accessToken = _tokenService.GenerateAccessToken(user);

        var responseDto = new AuthResponseDto
        {
            AccessToken = accessToken,
            ExpiresInMinutes = 15,
            User = new UserProfileDto
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                CreatedAtUtc = user.CreatedAtUtc
            }
        };

        return AuthResult.Success(responseDto, rawRefreshToken, expiresAtUtc, StatusCodes.Status200OK);
    }
}
