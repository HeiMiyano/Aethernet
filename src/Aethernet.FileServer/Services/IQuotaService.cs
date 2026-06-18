namespace Aethernet.FileServer.Services;

public interface IQuotaService
{
    Task<(long Used, long Quota, int Files)> GetForUserAsync(string uid, CancellationToken ct);
    Task<bool> CanAcceptAsync(string uid, long incomingBytes, CancellationToken ct);
}
