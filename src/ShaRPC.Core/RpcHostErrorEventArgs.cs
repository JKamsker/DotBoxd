namespace ShaRPC.Core;

/// <summary>
/// Describes a non-cancellation error observed by an <see cref="RpcHost"/> accept loop.
/// </summary>
public sealed class RpcHostErrorEventArgs : EventArgs
{
    public RpcHostErrorEventArgs(Exception exception)
    {
        Error = exception;
    }

    /// <summary>The accept-loop exception.</summary>
    public Exception Error { get; }
}
