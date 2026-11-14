using AuthFlow.Api.DTOs;

namespace AuthFlow.Api.Services;

public interface IAuthService
{
    Task<AuthResult> RegisterAsync(RegisterRequestDto request);

    Task<AuthResult> LoginAsync(LoginRequestDto request);

    Task<AuthResult> RefreshTokenAsync(string? rawRefreshToken);

    Task<AuthResult> LogoutAsync(string? rawRefreshToken);

    Task<UserProfileDto?> GetUserProfileAsync(Guid userId);
}
