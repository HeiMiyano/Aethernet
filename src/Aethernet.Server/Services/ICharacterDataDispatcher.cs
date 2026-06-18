using Aethernet.API.Dto;
using Aethernet.Server.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Aethernet.Server.Services;

public interface ICharacterDataDispatcher
{
    Task DispatchAsync(string fromUid, IReadOnlyList<string> recipientUids,
        CharacterDataDto data, IHubCallerClients<IAethernetHubClient> clients);
}
