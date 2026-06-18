namespace Aethernet.AuthService.Services;

public interface IRefreshTokenService
{
    Task<(string TokenId, string Plain)> IssueAsync(string uid, string? userAgent, string? ip);
    Task<string?> ValidateAndRotateAsync(string refreshToken, string? userAgent, string? ip);
    Task RevokeAllForUserAsync(string uid);
}
