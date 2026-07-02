using DotBoxD.Services.Attributes;

namespace DotBoxD.Services.Tests.GeneratedFixtures;

[DotBoxDService]
public interface IGeneratedStreamingService
{
    IAsyncEnumerable<int> Numbers(CancellationToken ct = default);

    Task<IAsyncEnumerable<int>> NumbersAsync(CancellationToken ct = default);

    Task<int> UploadBytesAsync(
        Stream bytes,
        CancellationToken ct = default);

    Task<int> UploadAsync(
        Stream bytes,
        IAsyncEnumerable<int> items,
        CancellationToken ct = default);

    IAsyncEnumerable<int> StreamUploadAsync(
        Stream bytes,
        IAsyncEnumerable<int> items,
        CancellationToken ct = default);
}
