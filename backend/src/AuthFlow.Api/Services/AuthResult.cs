using AuthFlow.Api.DTOs;

namespace AuthFlow.Api.Services;

public class AuthResult
{
    public bool Succeeded { get; set; }
    public int StatusCode { get; set; }
    public string? ErrorTitle { get; set; }
    public string? ErrorDetail { get; set; }
    public AuthResponseDto? Response { get; set; }
    public string? RawRefreshToken { get; set; }
    public DateTime? RefreshTokenExpiresAtUtc { get; set; }

    public static AuthResult Success(AuthResponseDto response, string rawRefreshToken, DateTime expiresAtUtc, int statusCode = 200)
    {
        return new AuthResult
        {
            Succeeded = true,
            StatusCode = statusCode,
            Response = response,
            RawRefreshToken = rawRefreshToken,
            RefreshTokenExpiresAtUtc = expiresAtUtc
        };
    }

    public static AuthResult Failure(int statusCode, string title, string detail)
    {
        return new AuthResult
        {
            Succeeded = false,
            StatusCode = statusCode,
            ErrorTitle = title,
            ErrorDetail = detail
        };
    }
}
