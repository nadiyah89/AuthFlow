namespace AuthFlow.Api.DTOs;

public class AuthResponseDto
{
    public string AccessToken { get; set; } = string.Empty;
    public int ExpiresInMinutes { get; set; }
    public UserProfileDto User { get; set; } = null!;
}
