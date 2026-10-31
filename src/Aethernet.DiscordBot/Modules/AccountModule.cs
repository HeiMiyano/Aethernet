using Aethernet.Data;
using Aethernet.Data.Entities;
using Aethernet.DiscordBot.Services;
using Aethernet.Shared.Identity;
using Discord;
using Discord.Interactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Aethernet.DiscordBot.Modules;

/// <summary>
/// Bot-DM recovery and account status. Accounts themselves are created only by "Sign in with
/// Discord" in the plugin (AuthService's DiscordAuthController) — there's deliberately no
/// /register here, so there's exactly one signup path. This is the bot half of account management — the
/// account itself still lives in the same Postgres database the other three services use; the
/// bot talks to it directly (same pattern as AdminController/AethernetHub reaching the DB
/// straight from the Server project, rather than hopping through AuthService's HTTP API).
/// Credentials always go to a DM, never into the slash-command's public channel reply.
/// </summary>
public sealed class AccountModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly AethernetDbContext _db;
    private readonly IConfiguration _cfg;

    public AccountModule(AethernetDbContext db, IConfiguration cfg) { _db = db; _cfg = cfg; }

    private string DiscordId => Context.User.Id.ToString();

    private const string NoAccountMessage =
        "No Aethernet account is linked to your Discord account. In game, open `/aethernet wizard` and " +
        "click **Sign in with Discord** to create one (or **Link Discord** if you already have an account).";

    private int ClaimLifetimeMinutes =>
        int.TryParse(_cfg["Discord:ClaimTokenLifetimeMinutes"], out var m) ? m : 15;

    [SlashCommand("recover", "DM yourself a one-time recovery token for your linked Aethernet account.")]
    public async Task RecoverAsync()
    {
        await DeferAsync(ephemeral: true);

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.DiscordUserId == DiscordId);
        if (user is null)
        {
            await FollowupAsync(NoAccountMessage, ephemeral: true);
            return;
        }
        if (user.IsBanned)
        {
            await FollowupAsync("This account is banned; recovery is disabled.", ephemeral: true);
            return;
        }

        var lifetime = ClaimLifetimeMinutes;
        var token    = ClaimTokenGenerator.NewToken();
        _db.DiscordRecoveryClaims.Add(new DiscordRecoveryClaimEntity
        {
            DiscordUserId = DiscordId,
            Uid           = user.Uid,
            TokenHash     = ClaimTokenGenerator.Hash(token),
            CreatedAt     = DateTime.UtcNow,
            ExpiresAt     = DateTime.UtcNow.AddMinutes(lifetime),
        });
        await _db.SaveChangesAsync();

        var body =
            $"**Account recovery requested** for UID `{user.Uid}`.\n" +
            $"Run `/claim token:{token}` within {lifetime} minutes to get a new secret key. " +
            "This invalidates your current secret key — only do this if you actually lost it. " +
            "Didn't request this? Ignore it; the token expires on its own.";
        if (await TryDmAsync(body))
            await FollowupAsync("Check your DMs for a one-time claim token.", ephemeral: true);
        else
            await FollowupAsync("I couldn't DM you — enable DMs from server members (or this bot) and try again.", ephemeral: true);
    }

    [SlashCommand("claim", "Redeem a one-time recovery token (from /recover) for a new secret key.")]
    public async Task ClaimAsync([Summary("token", "The token sent to your DMs by /recover")] string token)
    {
        await DeferAsync(ephemeral: true);

        var hash = ClaimTokenGenerator.Hash(token);
        var claim = await _db.DiscordRecoveryClaims.FirstOrDefaultAsync(c =>
            c.DiscordUserId == DiscordId && c.TokenHash == hash &&
            c.UsedAt == null && c.ExpiresAt > DateTime.UtcNow);
        if (claim is null)
        {
            await FollowupAsync("That token is invalid, already used, or expired. Run `/recover` to get a new one.", ephemeral: true);
            return;
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Uid == claim.Uid);
        if (user is null)
        {
            await FollowupAsync("That account no longer exists.", ephemeral: true);
            return;
        }

        claim.UsedAt = DateTime.UtcNow;
        var newKey = UidGenerator.NewSecretKey();
        user.SecretKeyHash = SecretKeyHasher.Hash(newKey);
        await _db.SaveChangesAsync();

        var body = $"**New secret key issued** for UID `{user.Uid}`:\n`{newKey}`\n\nYour old secret key no longer works. Save this one now — it won't be shown again.";
        if (await TryDmAsync(body))
            await FollowupAsync("Check your DMs for your new secret key.", ephemeral: true);
        else
            await FollowupAsync($"I couldn't DM you, so here it is — treat this as sensitive:\n{body}", ephemeral: true);
    }

    [SlashCommand("whoami", "Show your linked Aethernet account status.")]
    public async Task WhoAmIAsync()
    {
        await DeferAsync(ephemeral: true);

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.DiscordUserId == DiscordId);
        if (user is null)
        {
            await FollowupAsync(NoAccountMessage, ephemeral: true);
            return;
        }

        var status = user.IsBanned ? $"**banned** ({user.BanReason ?? "no reason given"})" : "active";
        await FollowupAsync(
            $"UID: `{user.Uid}`\nAlias: {user.Alias ?? "(none set)"}\nStatus: {status}\nLinked since: {user.CreatedAt:yyyy-MM-dd}",
            ephemeral: true);
    }

    [SlashCommand("unlink", "Unlink your Discord account from your Aethernet account.")]
    public async Task UnlinkAsync()
    {
        await DeferAsync(ephemeral: true);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.DiscordUserId == DiscordId);
        if (user is null)
        {
            await FollowupAsync("No Aethernet account is linked to your Discord account.", ephemeral: true);
            return;
        }

        user.DiscordUserId = null;
        await _db.SaveChangesAsync();
        await FollowupAsync(
            $"Unlinked UID `{user.Uid}` from your Discord account. That account can't connect until it's " +
            "linked to a Discord account again — use **Link Discord** in the Aethernet plugin.", ephemeral: true);
    }

    private async Task<bool> TryDmAsync(string message)
    {
        try
        {
            var dm = await Context.User.CreateDMChannelAsync();
            await dm.SendMessageAsync(message);
            return true;
        }
        catch (Discord.Net.HttpException)
        {
            // DMs closed to this bot/server — caller falls back to the ephemeral reply.
            return false;
        }
    }
}
