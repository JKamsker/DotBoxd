using DotBoxD.Services.Attributes;

namespace DotBoxD.Kernels.Game.Server.Abstractions.Ipc;

/// <summary>
/// Server → plugin callback for remote <c>RunLocal</c> terminals. The server runs the lowered
/// <c>Where</c>/<c>Select</c> filter+projection in its sandbox and, for each event that passes the filter,
/// pushes ONLY the encoded projected value back to the plugin keyed by subscription id; the plugin decodes it
/// and invokes its native <c>RunLocal</c> delegate. This is the reverse direction of
/// <see cref="IGamePluginControlService"/>: the PLUGIN provides it and the SERVER calls into it over the same
/// connection (the bidirectional peer makes this one wire serve both directions).
/// </summary>
[DotBoxDService]
public interface IPluginEventCallback
{
    ValueTask OnEventAsync(string subscriptionId, ReadOnlyMemory<byte> projectedValue, CancellationToken ct = default);

    ValueTask<byte[]> OnResultAsync(string subscriptionId, ReadOnlyMemory<byte> contextValue, CancellationToken ct = default);
}
