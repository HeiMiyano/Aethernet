using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aethernet.DiscordBot.Services;

/// <summary>
/// Owns the Discord gateway connection and slash-command dispatch for the lifetime of the
/// process. Each interaction gets its own DI scope (so <c>AethernetDbContext</c> — scoped,
/// not singleton — behaves the same way here as it does per-request in the web services).
/// </summary>
public sealed class BotHostedService : BackgroundService
{
    private readonly DiscordSocketClient _client;
    private readonly InteractionService _interactions;
    private readonly IServiceProvider _services;
    private readonly IConfiguration _cfg;
    private readonly ILogger<BotHostedService> _log;

    public BotHostedService(
        DiscordSocketClient client, InteractionService interactions, IServiceProvider services,
        IConfiguration cfg, ILogger<BotHostedService> log)
    {
        _client = client; _interactions = interactions; _services = services; _cfg = cfg; _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _client.Log += LogDiscordAsync;
        _interactions.Log += LogDiscordAsync;
        _client.Ready += OnReadyAsync;
        _client.InteractionCreated += OnInteractionCreatedAsync;

        await _interactions.AddModulesAsync(typeof(Program).Assembly, _services);

        var token = _cfg["Discord:BotToken"];
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Discord:BotToken is required");

        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
    }

    private async Task OnReadyAsync()
    {
        var guildIdStr = _cfg["Discord:GuildId"];
        if (!string.IsNullOrWhiteSpace(guildIdStr) && ulong.TryParse(guildIdStr, out var guildId))
        {
            // Guild-scoped registration propagates in seconds — use this while iterating on commands.
            await _interactions.RegisterCommandsToGuildAsync(guildId);
            _log.LogInformation("Slash commands registered to guild {GuildId}", guildId);
        }
        else
        {
            // Global registration can take up to ~an hour to show up everywhere. Fine for prod,
            // annoying for dev — set Discord:GuildId while iterating.
            await _interactions.RegisterCommandsGloballyAsync();
            _log.LogInformation("Slash commands registered globally");
        }
    }

    private Task OnInteractionCreatedAsync(SocketInteraction interaction)
    {
        // Discord.Net's InteractionCreated handler must return promptly; do the real work on a
        // background task so one slow command can't stall the gateway heartbeat.
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _services.CreateScope();
                var ctx = new SocketInteractionContext(_client, interaction);
                await _interactions.ExecuteCommandAsync(ctx, scope.ServiceProvider);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "unhandled error while executing interaction {Id}", interaction.Id);
            }
        });
        return Task.CompletedTask;
    }

    private Task LogDiscordAsync(LogMessage msg)
    {
        var level = msg.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error    => LogLevel.Error,
            LogSeverity.Warning  => LogLevel.Warning,
            LogSeverity.Info     => LogLevel.Information,
            LogSeverity.Verbose  => LogLevel.Debug,
            _                    => LogLevel.Trace,
        };
        _log.Log(level, msg.Exception, "[{Source}] {Message}", msg.Source, msg.Message);
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await _client.StopAsync();
        await base.StopAsync(cancellationToken);
    }
}
