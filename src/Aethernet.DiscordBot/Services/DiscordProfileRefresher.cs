using Aethernet.Data;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aethernet.DiscordBot.Services;

/// <summary>
/// Keeps Users.DiscordUsername / DiscordDisplayName current for the admin dashboard.
/// The auth service records them on every Discord sign-in/link; this fills in accounts linked
/// before that existed and picks up renames for people who rarely sign in again.
///
/// Uses the bot token's REST access (GET /users/{id}), which works for any Discord user ID — no
/// privileged gateway intents needed. Deliberately slow (one lookup per second, small batches,
/// every few hours) so it never competes with slash commands for rate-limit budget.
/// </summary>
public sealed class DiscordProfileRefresher : BackgroundService
{
    private static readonly TimeSpan FirstRunDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Interval      = TimeSpan.FromHours(6);
    private static readonly TimeSpan StaleAfter    = TimeSpan.FromDays(1);
    private static readonly TimeSpan PerLookup     = TimeSpan.FromSeconds(1);
    private const int BatchSize = 200;

    private readonly DiscordSocketClient _client;
    private readonly IServiceProvider _services;
    private readonly ILogger<DiscordProfileRefresher> _log;

    public DiscordProfileRefresher(DiscordSocketClient client, IServiceProvider services, ILogger<DiscordProfileRefresher> log)
    {
        _client = client; _services = services; _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(FirstRunDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RefreshBatchAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { _log.LogWarning(ex, "Discord profile refresh pass failed"); }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task RefreshBatchAsync(CancellationToken ct)
    {
        if (_client.LoginState != LoginState.LoggedIn)
        {
            _log.LogDebug("Discord profile refresh skipped: bot not logged in yet");
            return;
        }

        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AethernetDbContext>();
        var staleBefore = DateTime.UtcNow - StaleAfter;

        // Never-fetched first, then oldest.
        var due = await db.Users
            .Where(u => u.DiscordUserId != null &&
                        (u.DiscordProfileUpdatedAt == null || u.DiscordProfileUpdatedAt < staleBefore))
            .OrderBy(u => u.DiscordProfileUpdatedAt != null).ThenBy(u => u.DiscordProfileUpdatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);
        if (due.Count == 0) return;

        int updated = 0, missing = 0;
        foreach (var user in due)
        {
            if (!ulong.TryParse(user.DiscordUserId, out var id)) continue;
            try
            {
                var du = await _client.Rest.GetUserAsync(id);
                if (du is null) { missing++; user.DiscordProfileUpdatedAt = DateTime.UtcNow; continue; }
                user.DiscordUsername         = Truncate(du.Username, 32);
                user.DiscordDisplayName      = Truncate(du.GlobalName, 64);
                user.DiscordProfileUpdatedAt = DateTime.UtcNow;
                updated++;
            }
            catch (HttpException ex)
            {
                // Deleted Discord account (404) etc. — stamp it so we don't retry every pass.
                _log.LogDebug("Discord lookup for {DiscordId} failed: {Status}", id, ex.HttpCode);
                user.DiscordProfileUpdatedAt = DateTime.UtcNow;
                missing++;
            }
            await Task.Delay(PerLookup, ct);
        }
        await db.SaveChangesAsync(ct);
        _log.LogInformation("Discord profile refresh: {Updated} updated, {Missing} not found ({Due} due)", updated, missing, due.Count);
    }

    private static string? Truncate(string? s, int max) =>
        string.IsNullOrWhiteSpace(s) ? null : (s.Length <= max ? s : s[..max]);
}
