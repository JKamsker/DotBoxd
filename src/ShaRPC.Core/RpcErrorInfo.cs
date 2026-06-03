namespace ShaRPC.Core;

/// <summary>
/// The error returned to a remote caller for a handler exception, produced by
/// <see cref="RpcPeerOptions.ExceptionTransformer"/> on the side that runs the service.
/// <see cref="Message"/> becomes the caller's <c>ShaRpcRemoteException</c> message and
/// <see cref="Type"/> its remote error-type name (e.g. a value from <see cref="RpcErrorTypes"/> or a
/// custom application error code).
/// </summary>
public readonly record struct RpcErrorInfo(string Message, string Type)
{
    /// <summary>
    /// Builds an <see cref="RpcErrorInfo"/> that exposes the exception's message and runtime type
    /// name — the "expose everything" shortcut for a transformer. Prefer a tailored transformer that
    /// returns safe, caller-facing messages: exception text can carry sensitive detail (file paths,
    /// connection strings), so only expose it to trusted peers.
    /// </summary>
    public static RpcErrorInfo FromException(Exception exception) =>
        new(exception.Message, exception.GetType().Name);
}
