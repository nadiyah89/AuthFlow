using AuthFlow.Api.DTOs;

namespace AuthFlow.Api.Services;

public interface IAuthService
{
    Task<AuthResult> RegisterAsync(RegisterRequestDto request);

    Task<AuthResult> LoginAsync(LoginRequestDto request);
}
