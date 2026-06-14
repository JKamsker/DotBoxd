using System.IO.Pipelines;
using ShaRPC.Core.Protocol;

namespace ShaRPC.Core.Streaming;

/// <summary>
/// Gives generated dispatchers access to streamed arguments and streamed responses.
/// </summary>
public interface IRpcStreamingContext
{
    Stream GetStream(RpcStreamHandle handle);

    Pipe GetPipe(RpcStreamHandle handle);

    IAsyncEnumerable<T> GetAsyncEnumerable<T>(RpcStreamHandle handle);

    void SetResponse(Stream stream);

    void SetResponse(Pipe pipe);

    void SetResponse<T>(IAsyncEnumerable<T> items);
}
