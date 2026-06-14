using DotBoxd.Services.Attributes;

namespace DotBoxd.Services.Tests.GeneratedFixtures;

[DotBoxdService]
public interface IGeneratedStreamingService
{
    IAsyncEnumerable<int> Numbers(CancellationToken ct = default);

    Task<IAsyncEnumerable<int>> NumbersAsync(CancellationToken ct = default);

    Task<int> UploadAsync(
        Stream bytes,
        IAsyncEnumerable<int> items,
        CancellationToken ct = default);

    IAsyncEnumerable<int> StreamUploadAsync(
        Stream bytes,
        IAsyncEnumerable<int> items,
        CancellationToken ct = default);
}
