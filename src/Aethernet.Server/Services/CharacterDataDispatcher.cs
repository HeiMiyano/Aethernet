using Aethernet.API.Dto;
using Aethernet.Server.Hubs;
using Aethernet.Shared.Observability;
using Microsoft.AspNetCore.SignalR;
using Prometheus;  // for Histogram.NewTimer() extension

namespace Aethernet.Server.Services;

/// <summary>
/// Fans out a CharacterDataDto from one user to many. We deliberately do nothing fancy here —
/// the hub already filtered the recipient list down to active pairs, so we just iterate.
/// (If the recipient list ever grows past ~50 we could batch into groups; for now keep it
/// straightforward and observable.)
/// </summary>
public sealed class CharacterDataDispatcher : ICharacterDataDispatcher
{
    private readonly ILogger<CharacterDataDispatcher> _log;
    public CharacterDataDispatcher(ILogger<CharacterDataDispatcher> log) { _log = log; }

    public async Task DispatchAsync(string fromUid, IReadOnlyList<string> recipientUids,
        CharacterDataDto data, IHubCallerClients<IAethernetHubClient> clients)
    {
        var envelope = new OnlineUserCharaDataMessageDto(new UserDto(fromUid), data);
        _log.LogDebug("dispatch from {From} -> {Count} recipients, dataVersion={Version}",
            fromUid, recipientUids.Count, data.DataVersion);

        // Send in parallel — SignalR's invocation queue is per-connection so we get good throughput.
        using (AethernetMetrics.HubPushDuration.NewTimer())
        {
            AethernetMetrics.HubPushRecipients.Inc(recipientUids.Count);
            var tasks = recipientUids.Select(uid => clients.User(uid).Client_UserReceiveCharacterData(envelope));
            await Task.WhenAll(tasks);
        }
    }
}
