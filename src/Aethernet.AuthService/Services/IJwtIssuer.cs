namespace Aethernet.AuthService.Services;

public interface IJwtIssuer
{
    (string Token, DateTime ExpiresAt) IssueAccessToken(string uid, bool isAdmin, bool isModerator);
}
