using Aethernet.API.Dto;

namespace Aethernet.AuthService.Services;

public interface IRegistrationService
{
    Task<RegisterResponseDto> RegisterAsync(RegisterRequestDto request, string? discordUserId);
    Task<RegisterResponseDto> RotateSecretAsync(string uid);
    Task<RegisterResponseDto> RecoverAsync(string recoverySecret);
}
