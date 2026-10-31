namespace Aethernet.AuthService.Services;

/// <summary>Bound from the <c>Discord</c> configuration section (env: <c>Discord__*</c>).</summary>
public sealed class DiscordOAuthOptions
{
    /// <summary>OAuth2 client ID from the Discord Developer Portal (Application → OAuth2).</summary>
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Absolute URL of <c>/auth/discord/callback</c> on the public auth hostname. Must match one of
    /// the redirects registered in the Developer Portal character-for-character.
    /// </summary>
    public string? RedirectUri { get; set; }

    /// <summary>If set, only members of this Discord server can sign in. Blank = anyone with Discord.</summary>
    public string? RequiredGuildId { get; set; }

    /// <summary>Optional invite link shown to people who aren't in <see cref="RequiredGuildId"/> yet.</summary>
    public string? InviteUrl { get; set; }

    /// <summary>
    /// When true, /auth/login and /auth/refresh refuse accounts with no Discord link (403
    /// discord_link_required). Leave false until a plugin build with the Link button has shipped,
    /// otherwise old plugins get locked out with no way to link.
    /// </summary>
    public bool RequireLinkedAccount { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(ClientSecret) &&
        Uri.TryCreate(RedirectUri, UriKind.Absolute, out _);
}
