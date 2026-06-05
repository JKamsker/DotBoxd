using ShaRPC.Core.Attributes;

namespace Shared;

[ShaRpcService]
public interface IGeneratedStreamingService
{
    IAsyncEnumerable<int> Numbers(CancellationToken ct = default);

    Task<IAsyncEnumerable<int>> NumbersAsync(CancellationToken ct = default);

    Task<int> UploadAsync(
        Stream bytes,
        IAsyncEnumerable<int> items,
        CancellationToken ct = default);
}
