using ShaRPC.Core.Attributes;

namespace Shared;

/// <summary>
/// Callback contract a connecting peer provides so the other side can push notifications
/// back to it. Demonstrates the bidirectional peer model: the same connection carries
/// <see cref="IGameService"/> calls one way and <see cref="IPlayerNotifications"/> calls the other.
/// </summary>
[ShaRpcService]
public interface IPlayerNotifications
{
    Task NotifyAsync(string message, CancellationToken ct = default);

    Task<string> WhoAmIAsync(CancellationToken ct = default);
}
