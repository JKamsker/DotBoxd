using DotBoxD.Services.Attributes;

namespace Shared;

[DotBoxDService]
public interface IStreamedArgumentBenchmarkService
{
    Task<int> UploadBytesAsync(Stream bytes, CancellationToken ct = default);

    Task<int> UploadBothAsync(
        Stream bytes,
        IAsyncEnumerable<int> items,
        CancellationToken ct = default);
}
