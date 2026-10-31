using System.Collections.Concurrent;
using System.Security.Cryptography;
using Aethernet.API.Dto;

namespace Aethernet.AuthService.Services;

public sealed class DiscordSignInSession
{
    /// <summary>OAuth <c>state</c> — travels through the browser, so it can't be used to poll.</summary>
    public required string State { get; init; }
    /// <summary>Known only to the plugin that called /begin. The only way to read the result.</summary>
    public required string PollToken { get; init; }
    public required string Mode { get; init; }
    /// <summary>For <see cref="DiscordAuthModes.Link"/>: the account (already verified at /begin).</summary>
    public string? LinkUid { get; init; }
    public required DateTime ExpiresAt { get; init; }

    internal int Claimed;                       // 1 once a callback has started processing
    internal DiscordPollResponseDto? Result;    // set when the browser leg finishes
}

/// <summary>
/// In-memory hand-off between the browser leg (Discord redirects to /callback) and the plugin leg
/// (polls /poll). Deliberately not in Postgres: a pending result can hold a plaintext secret key
/// for a few seconds, and that should never touch disk. Cost of a restart mid-sign-in: the user
/// clicks the button again. Assumes a single auth-service instance (true for every compose file).
/// </summary>
public sealed class DiscordSignInStore
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    // /begin is unauthenticated, so cap how many pending sessions a flood can create.
    private const int MaxPending = 5000;

    private readonly ConcurrentDictionary<string, DiscordSignInSession> _byState = new();
    private readonly ConcurrentDictionary<string, DiscordSignInSession> _byPoll  = new();

    public DiscordSignInSession? Create(string mode, string? linkUid)
    {
        Sweep();
        if (_byPoll.Count >= MaxPending) return null;

        var s = new DiscordSignInSession
        {
            State     = NewToken(),
            PollToken = NewToken(),
            Mode      = mode,
            LinkUid   = linkUid,
            ExpiresAt = DateTime.UtcNow + Lifetime,
        };
        _byState[s.State]    = s;
        _byPoll[s.PollToken] = s;
        return s;
    }

    /// <summary>Finds a live session by OAuth state and marks it as being processed. Returns null
    /// for unknown/expired state, or if another request already claimed it (replayed callback).</summary>
    public DiscordSignInSession? Claim(string? state)
    {
        if (string.IsNullOrEmpty(state) || !_byState.TryRemove(state, out var s)) return null;
        if (s.ExpiresAt <= DateTime.UtcNow) return null;
        return Interlocked.Exchange(ref s.Claimed, 1) == 0 ? s : null;
    }

    public void Complete(DiscordSignInSession s, DiscordPollResponseDto result) =>
        Interlocked.CompareExchange(ref s.Result, result, null);

    public DiscordPollResponseDto Poll(string? pollToken)
    {
        if (string.IsNullOrEmpty(pollToken) || !_byPoll.TryGetValue(pollToken, out var s) || s.ExpiresAt <= DateTime.UtcNow)
        {
            if (!string.IsNullOrEmpty(pollToken)) _byPoll.TryRemove(pollToken, out _);
            return new DiscordPollResponseDto(DiscordPollStatus.Failed, null, null, false, "expired",
                "This sign-in attempt expired. Start again from the plugin.");
        }

        var result = Volatile.Read(ref s.Result);
        if (result is null)
            return new DiscordPollResponseDto(DiscordPollStatus.Pending, null, null, false, null, null);

        // One-shot: the (possibly secret-bearing) result is handed out exactly once.
        _byPoll.TryRemove(pollToken, out _);
        return result;
    }

    private void Sweep()
    {
        var now = DateTime.UtcNow;
        foreach (var (token, s) in _byPoll)
        {
            if (s.ExpiresAt > now) continue;
            _byPoll.TryRemove(token, out _);
            _byState.TryRemove(s.State, out _);
        }
    }

    private static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
